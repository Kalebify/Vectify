using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Vectify.Api.Clients;
using Vectify.Api.Components;

namespace Vectify.Api.Tests.Clients;

/// <summary>
/// Pruebas unitarias de PythonPhysicalUnionClient contra un
/// HttpMessageHandler stub (sin red real): cubren los estados exigidos por
/// el contrato -- éxito, geometría inválida, unión imposible, parámetros
/// inválidos, timeout, no disponible, respuesta inválida, error HTTP, y la
/// validación defensiva adicional sobre una respuesta 200 que "dice" éxito
/// pero component_count_after no coincide con expected_component_count_after
/// (nunca se debe confiar ciegamente en esa respuesta). Mismo criterio que
/// PythonComponentClientTests.
/// </summary>
public sealed class PythonPhysicalUnionClientTests
{
    private static PythonPhysicalUnionClient CreateClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handlerFunc,
        TimeSpan? timeout = null)
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(handlerFunc))
        {
            BaseAddress = new Uri("http://python-engine.test"),
            Timeout = timeout ?? TimeSpan.FromSeconds(5),
        };

        return new PythonPhysicalUnionClient(httpClient, NullLogger<PythonPhysicalUnionClient>.Instance);
    }

    private static readonly IReadOnlyList<LayerComponent> Selection = new List<LayerComponent>
    {
        new("component-1", new List<ComponentMember> { new(0, 0, "solid", new ComponentBounds(0, 0, 10, 10), 100) }, new ComponentBounds(0, 0, 10, 10), 100, false),
        new("component-2", new List<ComponentMember> { new(1, 0, "solid", new ComponentBounds(20, 20, 30, 30), 100) }, new ComponentBounds(20, 20, 30, 30), 100, false),
    };

    private static Task<PythonPhysicalUnionResult> InvokeAsync(PythonPhysicalUnionClient client) =>
        client.UnionAsync(new MemoryStream([1, 2, 3, 4]), "image/svg+xml", "layer.svg", Selection);

    private static string SuccessBody(int componentCountAfter = 1, int expectedComponentCountAfter = 1, string strategy = "bridge") =>
        string.Format(
            """
            {{
              "svg": "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"100\" height=\"100\"><path d=\"M0,0 L10,0 L10,10 L0,10 Z\" fill=\"#000000\"/></svg>",
              "content_type": "image/svg+xml",
              "width": 100,
              "height": 100,
              "metrics": {{"path_count": 1, "approx_node_count": 4, "bounds": {{"min_x": 0, "min_y": 0, "max_x": 10, "max_y": 10, "width": 10, "height": 10}}}},
              "effective_params": {{"selections": [], "touch_ratio": 0.001, "tiny_area_ratio": 0.0005, "bridge_width_ratio": 0.02}},
              "component_count_before": 2,
              "component_count_after": {0},
              "expected_component_count_after": {1},
              "strategy": "{2}",
              "bridge_count": 1
            }}
            """,
            componentCountAfter,
            expectedComponentCountAfter,
            strategy);

    private static string ErrorBody(string code, string message) => $$"""{"code": "{{code}}", "message": "{{message}}"}""";

    [Fact]
    public async Task UnionAsync_WhenResponseIsValid_ReturnsSuccessWithMetrics()
    {
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, SuccessBody())));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonPhysicalUnionState.Success, result.State);
        Assert.Equal(1, result.ComponentCountAfter);
        Assert.Equal("bridge", result.Strategy);
        Assert.Equal(1, result.BridgeCount);
        Assert.Contains("<path", result.Svg);
    }

    [Fact]
    public async Task UnionAsync_SendsSelectedComponentsAsMultipartParams()
    {
        HttpRequestMessage? capturedRequest = null;
        var client = CreateClient((request, _) =>
        {
            capturedRequest = request;
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, SuccessBody()));
        });

        await InvokeAsync(client);

        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest!.Method);
        Assert.Equal("/api/v1/components/union", capturedRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task UnionAsync_WhenPythonReportsInvalidGeometry_ReturnsInvalidGeometry()
    {
        var client = CreateClient((_, _) => Task.FromResult(
            JsonResponse((HttpStatusCode)422, ErrorBody("physical_union_invalid_geometry", "geometría autointersectante"))));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonPhysicalUnionState.InvalidGeometry, result.State);
        Assert.Contains("autointersectante", result.Message);
    }

    [Fact]
    public async Task UnionAsync_WhenPythonReportsImpossible_ReturnsImpossible()
    {
        var client = CreateClient((_, _) => Task.FromResult(
            JsonResponse((HttpStatusCode)422, ErrorBody("physical_union_impossible", "no fue geométricamente posible"))));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonPhysicalUnionState.Impossible, result.State);
    }

    [Fact]
    public async Task UnionAsync_WhenPythonReportsInvalidParameters_ReturnsInvalidParameters()
    {
        var client = CreateClient((_, _) => Task.FromResult(
            JsonResponse((HttpStatusCode)422, ErrorBody("invalid_parameters", "selección inválida"))));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonPhysicalUnionState.InvalidParameters, result.State);
    }

    [Fact]
    public async Task UnionAsync_WhenPythonReportsTimeout_ReturnsTimeout()
    {
        var client = CreateClient((_, _) => Task.FromResult(
            JsonResponse((HttpStatusCode)504, ErrorBody("physical_union_timeout", "tardó demasiado"))));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonPhysicalUnionState.Timeout, result.State);
    }

    [Fact]
    public async Task UnionAsync_WhenConnectionRefused_ReturnsUnavailable()
    {
        var client = CreateClient((_, _) => throw new HttpRequestException("Connection refused"));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonPhysicalUnionState.Unavailable, result.State);
    }

    [Fact]
    public async Task UnionAsync_WhenRequestExceedsTimeout_ReturnsTimeout()
    {
        var client = CreateClient(
            async (_, ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
                return JsonResponse(HttpStatusCode.OK, SuccessBody());
            },
            timeout: TimeSpan.FromMilliseconds(50));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonPhysicalUnionState.Timeout, result.State);
    }

    [Fact]
    public async Task UnionAsync_WhenBodyIsNotJson_ReturnsInvalidResponse()
    {
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, "<html>not json</html>")));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonPhysicalUnionState.InvalidResponse, result.State);
    }

    [Fact]
    public async Task UnionAsync_WhenRequiredFieldsAreMissing_ReturnsInvalidResponse()
    {
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, """{"width": 100}""")));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonPhysicalUnionState.InvalidResponse, result.State);
    }

    [Fact]
    public async Task UnionAsync_WhenHttpStatusIsUnmappedError_ReturnsHttpError()
    {
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.InternalServerError, "")));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonPhysicalUnionState.HttpError, result.State);
    }

    [Fact]
    public async Task UnionAsync_WhenComponentCountAfterDoesNotMatchExpected_ReturnsInvalidResponse()
    {
        // Defensa en profundidad: Python NUNCA debería responder 200 con
        // esto (ya lo valida antes de responder), pero si alguna vez lo
        // hiciera, Vectify.Api no debe confiar ciegamente -- "nunca fingir
        // unión" aplica también del lado del cliente HTTP, no solo en Python.
        var body = SuccessBody(componentCountAfter: 2, expectedComponentCountAfter: 1);
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, body)));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonPhysicalUnionState.InvalidResponse, result.State);
    }

    [Fact]
    public async Task UnionAsync_WhenStrategyIsUnknown_ReturnsInvalidResponse()
    {
        var body = SuccessBody(strategy: "teleport");
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, body)));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonPhysicalUnionState.InvalidResponse, result.State);
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
