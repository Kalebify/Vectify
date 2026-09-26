using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Vectify.Api.Clients;
using Vectify.Api.Options;

namespace Vectify.Api.Tests.Clients;

/// <summary>
/// Pruebas unitarias de PythonVectorLayerClient contra un HttpMessageHandler
/// stub (sin red real, sin motor Python): cubren los estados exigidos por el
/// contrato y la validación defensiva adicional propia de este endpoint
/// batch -- la cantidad de capas devueltas y sus group_id deben coincidir
/// exactamente con las máscaras enviadas. Mismo criterio que
/// PythonVectorizeClientTests/PythonColorPaletteClientTests.
/// </summary>
public sealed class PythonVectorLayerClientTests
{
    private static readonly Guid GroupA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid GroupB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static PythonVectorLayerClient CreateClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handlerFunc,
        TimeSpan? timeout = null)
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(handlerFunc))
        {
            BaseAddress = new Uri("http://python-engine.test"),
            Timeout = timeout ?? TimeSpan.FromSeconds(5),
        };

        return new PythonVectorLayerClient(httpClient, Microsoft.Extensions.Options.Options.Create(new VectorLayerOptions()), NullLogger<PythonVectorLayerClient>.Instance);
    }

    private static IReadOnlyList<(Guid GroupId, Stream Content, string ContentType)> SampleMasks() =>
    [
        (GroupA, new MemoryStream([1, 2, 3]), "image/png"),
        (GroupB, new MemoryStream([4, 5, 6]), "image/png"),
    ];

    private static Task<PythonVectorLayerBatchResult> InvokeAsync(PythonVectorLayerClient client) =>
        client.VectorizeLayersAsync(SampleMasks());

    private static string LayerJson(Guid groupId, string svg = SvgFixture, string contentType = "image/svg+xml", int width = 10, int height = 10) =>
        $$"""
        {
          "group_id": "{{groupId:N}}",
          "svg": "{{svg}}",
          "content_type": "{{contentType}}",
          "width": {{width}},
          "height": {{height}},
          "metrics": {"path_count": 1, "approx_node_count": 4, "bounds": {"min_x": 2.0, "min_y": 2.0, "max_x": 8.0, "max_y": 8.0, "width": 6.0, "height": 6.0} }
        }
        """;

    private const string SvgFixture = "<svg xmlns=\\\"http://www.w3.org/2000/svg\\\" width=\\\"10\\\" height=\\\"10\\\"><path d=\\\"M2,2 L8,2 L8,8 L2,8 Z\\\"/></svg>";

    private static string SuccessBody(params Guid[] groupIds) =>
        $$"""{"layers": [{{string.Join(",", groupIds.Select(id => LayerJson(id)))}}]}""";

    [Fact]
    public async Task VectorizeLayersAsync_WhenResponseIsValid_ReturnsSuccessWithOneLayerPerMask()
    {
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, SuccessBody(GroupA, GroupB))));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonVectorLayerState.Success, result.State);
        Assert.NotNull(result.Layers);
        Assert.Equal(2, result.Layers!.Count);
        Assert.Contains(result.Layers, l => l.GroupId == GroupA);
        Assert.Contains(result.Layers, l => l.GroupId == GroupB);
    }

    [Fact]
    public async Task VectorizeLayersAsync_WhenPythonReturnsCorruptImage_ReturnsCorruptImageState()
    {
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.BadRequest, """{"code": "corrupt_image", "message": "no se pudo decodificar"}""")));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonVectorLayerState.CorruptImage, result.State);
    }

    [Fact]
    public async Task VectorizeLayersAsync_WhenPythonReturnsEmptyMask_ReturnsEmptyMaskState()
    {
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(
            (HttpStatusCode)422, """{"code": "empty_mask", "message": "sin foreground"}""")));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonVectorLayerState.EmptyMask, result.State);
    }

    [Fact]
    public async Task VectorizeLayersAsync_WhenConnectionRefused_ReturnsUnavailable()
    {
        var client = CreateClient((_, _) => throw new HttpRequestException("Connection refused"));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonVectorLayerState.Unavailable, result.State);
    }

    [Fact]
    public async Task VectorizeLayersAsync_WhenRequestExceedsTimeout_ReturnsTimeout()
    {
        var client = CreateClient(
            async (_, ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
                return JsonResponse(HttpStatusCode.OK, SuccessBody(GroupA, GroupB));
            },
            timeout: TimeSpan.FromMilliseconds(50));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonVectorLayerState.Timeout, result.State);
    }

    [Fact]
    public async Task VectorizeLayersAsync_WhenBodyIsNotJson_ReturnsInvalidResponse()
    {
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, "<html>not json</html>")));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonVectorLayerState.InvalidResponse, result.State);
    }

    [Fact]
    public async Task VectorizeLayersAsync_WhenLayersFieldIsMissing_ReturnsInvalidResponse()
    {
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, "{}")));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonVectorLayerState.InvalidResponse, result.State);
    }

    [Fact]
    public async Task VectorizeLayersAsync_WhenLayerCountDoesNotMatchMaskCount_ReturnsInvalidSvgState()
    {
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, SuccessBody(GroupA))));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonVectorLayerState.InvalidSvg, result.State);
    }

    [Fact]
    public async Task VectorizeLayersAsync_WhenGroupIdDoesNotMatchAnySentMask_ReturnsInvalidSvgState()
    {
        var unknownGroup = Guid.NewGuid();
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, SuccessBody(GroupA, unknownGroup))));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonVectorLayerState.InvalidSvg, result.State);
    }

    [Fact]
    public async Task VectorizeLayersAsync_WhenContentTypeIsUnexpected_ReturnsInvalidSvgState()
    {
        var body = $$"""{"layers": [{{LayerJson(GroupA, contentType: "image/png")}}, {{LayerJson(GroupB)}}]}""";
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, body)));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonVectorLayerState.InvalidSvg, result.State);
    }

    [Fact]
    public async Task VectorizeLayersAsync_WhenHttpStatusIsUnmappedError_ReturnsHttpError()
    {
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.InternalServerError, "")));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonVectorLayerState.HttpError, result.State);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string body) => new(statusCode)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handlerFunc) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) => handlerFunc(request, cancellationToken);
    }
}
