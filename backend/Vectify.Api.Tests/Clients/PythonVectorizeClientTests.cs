using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Vectify.Api.Clients;
using Vectify.Api.Tests.TestSupport;

namespace Vectify.Api.Tests.Clients;

/// <summary>
/// Pruebas unitarias de PythonVectorizeClient contra un HttpMessageHandler
/// stub (sin red real): cubren los estados exigidos por el contrato --
/// éxito, máscara corrupta, dimensiones excedidas, máscara vacía, timeout,
/// no disponible, respuesta inválida y error HTTP -- sin excepciones sin
/// controlar escapando del cliente. Mismo criterio que
/// PythonThresholdClientTests.
/// </summary>
public sealed class PythonVectorizeClientTests
{
    private static PythonVectorizeClient CreateClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handlerFunc,
        TimeSpan? timeout = null)
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(handlerFunc))
        {
            BaseAddress = new Uri("http://python-engine.test"),
            Timeout = timeout ?? TimeSpan.FromSeconds(5),
        };

        return new PythonVectorizeClient(httpClient, NullLogger<PythonVectorizeClient>.Instance);
    }

    private static Task<PythonVectorizeResult> InvokeAsync(PythonVectorizeClient client) =>
        client.VectorizeAsync(new MemoryStream([1, 2, 3, 4]), "image/png", "mask.png");

    [Fact]
    public async Task VectorizeAsync_WhenResponseIsValid_ReturnsSuccessWithSvgAndMetrics()
    {
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, VectorizePayloads.SuccessBody())));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonVectorizeState.Success, result.State);
        Assert.NotNull(result.Svg);
        Assert.Contains("<path", result.Svg);
        Assert.Equal(10, result.Width);
        Assert.Equal(10, result.Height);
        Assert.Equal(1, result.Metrics!.PathCount);
        Assert.Equal(4, result.Metrics.ApproxNodeCount);
        Assert.Equal(6, result.Metrics.Bounds.Width);
    }

    [Fact]
    public async Task VectorizeAsync_WhenPythonReportsCorruptImage_ReturnsCorruptImage()
    {
        var client = CreateClient((_, _) => Task.FromResult(
            JsonResponse(HttpStatusCode.BadRequest, VectorizePayloads.ErrorBody("corrupt_image", "no decodificable"))));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonVectorizeState.CorruptImage, result.State);
    }

    [Fact]
    public async Task VectorizeAsync_WhenPythonReportsEmptyMask_ReturnsEmptyMask()
    {
        var client = CreateClient((_, _) => Task.FromResult(
            JsonResponse(HttpStatusCode.UnprocessableEntity, VectorizePayloads.ErrorBody("empty_mask", "sin foreground"))));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonVectorizeState.EmptyMask, result.State);
    }

    [Fact]
    public async Task VectorizeAsync_WhenPythonReportsDimensionsExceeded_ReturnsDimensionsExceeded()
    {
        var client = CreateClient((_, _) => Task.FromResult(
            JsonResponse(HttpStatusCode.RequestEntityTooLarge, VectorizePayloads.ErrorBody("dimensions_exceeded", "demasiado grande"))));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonVectorizeState.DimensionsExceeded, result.State);
    }

    [Fact]
    public async Task VectorizeAsync_WhenPythonReportsTimeout_ReturnsTimeout()
    {
        var client = CreateClient((_, _) => Task.FromResult(
            JsonResponse((HttpStatusCode)504, VectorizePayloads.ErrorBody("vectorization_timeout", "tardó demasiado"))));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonVectorizeState.Timeout, result.State);
    }

    [Fact]
    public async Task VectorizeAsync_WhenConnectionRefused_ReturnsUnavailable()
    {
        var client = CreateClient((_, _) => throw new HttpRequestException("Connection refused"));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonVectorizeState.Unavailable, result.State);
        Assert.NotNull(result.Message);
    }

    [Fact]
    public async Task VectorizeAsync_WhenRequestExceedsTimeout_ReturnsTimeout()
    {
        var client = CreateClient(
            async (_, ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
                return JsonResponse(HttpStatusCode.OK, VectorizePayloads.SuccessBody());
            },
            timeout: TimeSpan.FromMilliseconds(50));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonVectorizeState.Timeout, result.State);
    }

    [Fact]
    public async Task VectorizeAsync_WhenBodyIsNotJson_ReturnsInvalidResponse()
    {
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, "<html>not json</html>")));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonVectorizeState.InvalidResponse, result.State);
    }

    [Fact]
    public async Task VectorizeAsync_WhenRequiredFieldsAreMissing_ReturnsInvalidResponse()
    {
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, """{"svg": ""}""")));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonVectorizeState.InvalidResponse, result.State);
    }

    [Fact]
    public async Task VectorizeAsync_WhenHttpStatusIsUnmappedError_ReturnsHttpError()
    {
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.InternalServerError, "")));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonVectorizeState.HttpError, result.State);
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
