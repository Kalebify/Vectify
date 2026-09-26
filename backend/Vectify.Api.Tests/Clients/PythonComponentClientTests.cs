using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Vectify.Api.Clients;
using Vectify.Api.Tests.TestSupport;

namespace Vectify.Api.Tests.Clients;

/// <summary>
/// Pruebas unitarias de PythonComponentClient contra un HttpMessageHandler
/// stub (sin red real): cubren los estados exigidos por el contrato --
/// éxito, SVG de entrada inválido/demasiado grande, demasiados subpaths,
/// parámetros inválidos, timeout, no disponible, respuesta inválida, error
/// HTTP, y la validación defensiva adicional sobre respuestas 200 con
/// contenido sospechoso (componentes con forma incoherente, resumen que no
/// coincide con la cantidad real de componentes, roles desconocidos) -- sin
/// excepciones sin controlar escapando del cliente. Mismo criterio que
/// PythonCheckClientTests.
/// </summary>
public sealed class PythonComponentClientTests
{
    private static PythonComponentClient CreateClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handlerFunc,
        TimeSpan? timeout = null)
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(handlerFunc))
        {
            BaseAddress = new Uri("http://python-engine.test"),
            Timeout = timeout ?? TimeSpan.FromSeconds(5),
        };

        return new PythonComponentClient(httpClient, NullLogger<PythonComponentClient>.Instance);
    }

    private static Task<PythonComponentResult> InvokeAsync(PythonComponentClient client) =>
        client.AnalyzeAsync(new MemoryStream([1, 2, 3, 4]), "image/svg+xml", "layer.svg");

    [Fact]
    public async Task AnalyzeAsync_WhenResponseIsValid_ReturnsSuccessWithComponents()
    {
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, ComponentPayloads.SuccessBody())));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonComponentState.Success, result.State);
        Assert.Equal(0, result.SkippedPathCount);
        Assert.Single(result.Components!);
        Assert.Equal("solid", result.Components![0].Members[0].Role);
    }

    [Fact]
    public async Task AnalyzeAsync_WhenPythonReportsInvalidInputSvg_ReturnsInvalidInputSvg()
    {
        var client = CreateClient((_, _) => Task.FromResult(
            JsonResponse(HttpStatusCode.BadRequest, ComponentPayloads.ErrorBody("invalid_input_svg", "SVG corrupto"))));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonComponentState.InvalidInputSvg, result.State);
    }

    [Fact]
    public async Task AnalyzeAsync_WhenPythonReportsTooManySubpaths_ReturnsTooManySubpaths()
    {
        var client = CreateClient((_, _) => Task.FromResult(
            JsonResponse(HttpStatusCode.RequestEntityTooLarge, ComponentPayloads.ErrorBody("too_many_component_subpaths", "demasiados subpaths"))));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonComponentState.TooManySubpaths, result.State);
    }

    [Fact]
    public async Task AnalyzeAsync_WhenPythonReportsTimeout_ReturnsTimeout()
    {
        var client = CreateClient((_, _) => Task.FromResult(
            JsonResponse((HttpStatusCode)504, ComponentPayloads.ErrorBody("component_analysis_timeout", "tardó demasiado"))));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonComponentState.Timeout, result.State);
    }

    [Fact]
    public async Task AnalyzeAsync_WhenConnectionRefused_ReturnsUnavailable()
    {
        var client = CreateClient((_, _) => throw new HttpRequestException("Connection refused"));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonComponentState.Unavailable, result.State);
    }

    [Fact]
    public async Task AnalyzeAsync_WhenRequestExceedsTimeout_ReturnsTimeout()
    {
        var client = CreateClient(
            async (_, ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
                return JsonResponse(HttpStatusCode.OK, ComponentPayloads.SuccessBody());
            },
            timeout: TimeSpan.FromMilliseconds(50));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonComponentState.Timeout, result.State);
    }

    [Fact]
    public async Task AnalyzeAsync_WhenBodyIsNotJson_ReturnsInvalidResponse()
    {
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, "<html>not json</html>")));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonComponentState.InvalidResponse, result.State);
    }

    [Fact]
    public async Task AnalyzeAsync_WhenRequiredFieldsAreMissing_ReturnsInvalidResponse()
    {
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, """{"skipped_path_count": 0}""")));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonComponentState.InvalidResponse, result.State);
    }

    [Fact]
    public async Task AnalyzeAsync_WhenHttpStatusIsUnmappedError_ReturnsHttpError()
    {
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.InternalServerError, "")));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonComponentState.HttpError, result.State);
    }

    [Fact]
    public async Task AnalyzeAsync_WhenSummaryDoesNotMatchComponentCounts_ReturnsInvalidSvg()
    {
        var body = ComponentPayloads.SuccessBody(componentCount: 5);
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, body)));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonComponentState.InvalidSvg, result.State);
    }

    [Fact]
    public async Task AnalyzeAsync_WhenTinyCountDoesNotMatchActualIsTinyFlags_ReturnsInvalidSvg()
    {
        var body = ComponentPayloads.SuccessBody(tinyComponentCount: 1);
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, body)));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonComponentState.InvalidSvg, result.State);
    }

    [Fact]
    public async Task AnalyzeAsync_WhenMemberRoleIsUnknown_ReturnsInvalidSvg()
    {
        var body = ComponentPayloads.SuccessBody(
            componentsJson: $"[{ComponentPayloads.ComponentJson(membersJson: ComponentPayloads.MemberJson(role: "unknown"))}]");
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, body)));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonComponentState.InvalidSvg, result.State);
    }

    [Fact]
    public async Task AnalyzeAsync_WhenComponentBoundsAreIncoherent_MaxLessThanMin_ReturnsInvalidSvg()
    {
        var body = ComponentPayloads.SuccessBody(
            componentsJson: $"[{ComponentPayloads.ComponentJson(minX: 8, maxX: 2)}]");
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, body)));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonComponentState.InvalidSvg, result.State);
    }

    [Fact]
    public async Task AnalyzeAsync_WhenComponentHasNoMembers_ReturnsInvalidSvg()
    {
        var body = ComponentPayloads.SuccessBody(
            componentsJson: $"[{ComponentPayloads.ComponentJson(membersJson: "")}]");
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, body)));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonComponentState.InvalidSvg, result.State);
    }

    [Fact]
    public async Task AnalyzeAsync_WhenSkippedPathCountIsNegative_ReturnsInvalidSvg()
    {
        var body = ComponentPayloads.SuccessBody(skippedPathCount: -1);
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, body)));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonComponentState.InvalidSvg, result.State);
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
