using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Vectify.Api.Clients;
using Vectify.Api.Tests.TestSupport;
using Vectify.Api.Threshold;

namespace Vectify.Api.Tests.Clients;

/// <summary>
/// Pruebas unitarias de PythonThresholdClient contra un HttpMessageHandler
/// stub (sin red real, sin OpenCV): cubren los estados exigidos por el
/// contrato -- éxito, imagen corrupta, dimensiones excedidas, parámetros
/// inválidos, timeout, no disponible, respuesta inválida y error HTTP
/// genérico -- sin excepciones sin controlar escapando del cliente. Mismo
/// criterio que PythonPreprocessClientTests (M1-S03).
/// </summary>
public sealed class PythonThresholdClientTests
{
    private static readonly ThresholdParameters SampleParameters = new(140, true);

    private static PythonThresholdClient CreateClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handlerFunc,
        TimeSpan? timeout = null)
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(handlerFunc))
        {
            BaseAddress = new Uri("http://python-engine.test"),
            Timeout = timeout ?? TimeSpan.FromSeconds(5),
        };

        return new PythonThresholdClient(httpClient, NullLogger<PythonThresholdClient>.Instance);
    }

    private static Task<PythonThresholdResult> InvokeAsync(PythonThresholdClient client) =>
        client.ThresholdAsync(new MemoryStream([1, 2, 3]), "image/png", "preview.png", SampleParameters);

    [Fact]
    public async Task ThresholdAsync_WhenResponseIsValid_ReturnsSuccessWithDecodedMask()
    {
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            ThresholdPayloads.SuccessBody(value: 140, invert: true, width: 32, height: 16, foregroundPercent: 12.5, backgroundPercent: 87.5))));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonThresholdState.Success, result.State);
        Assert.NotNull(result.ImageBytes);
        Assert.NotEmpty(result.ImageBytes!);
        Assert.Equal("image/png", result.ContentType);
        Assert.Equal(32, result.Width);
        Assert.Equal(16, result.Height);
        Assert.Equal(new ThresholdParameters(140, true), result.EffectiveParameters);
        Assert.NotNull(result.Metrics);
        Assert.Equal(12.5, result.Metrics!.ForegroundPercent);
        Assert.Equal(87.5, result.Metrics.BackgroundPercent);
    }

    [Fact]
    public async Task ThresholdAsync_WhenPythonReturnsCorruptImage_ReturnsCorruptImageState()
    {
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.BadRequest, ThresholdPayloads.ErrorBody("corrupt_image", "no se pudo decodificar"))));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonThresholdState.CorruptImage, result.State);
    }

    [Fact]
    public async Task ThresholdAsync_WhenPythonReturnsDimensionsExceeded_ReturnsDimensionsExceededState()
    {
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(
            (HttpStatusCode)413, ThresholdPayloads.ErrorBody("dimensions_exceeded", "demasiado grande"))));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonThresholdState.DimensionsExceeded, result.State);
    }

    [Fact]
    public async Task ThresholdAsync_WhenPythonReturnsInvalidParameters_ReturnsInvalidParametersState()
    {
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(
            (HttpStatusCode)422, ThresholdPayloads.ErrorBody("invalid_parameters", "fuera de rango"))));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonThresholdState.InvalidParameters, result.State);
    }

    [Fact]
    public async Task ThresholdAsync_WhenConnectionRefused_ReturnsUnavailable()
    {
        var client = CreateClient((_, _) => throw new HttpRequestException("Connection refused"));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonThresholdState.Unavailable, result.State);
    }

    [Fact]
    public async Task ThresholdAsync_WhenRequestExceedsTimeout_ReturnsTimeout()
    {
        var client = CreateClient(
            async (_, ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
                return JsonResponse(HttpStatusCode.OK, ThresholdPayloads.SuccessBody());
            },
            timeout: TimeSpan.FromMilliseconds(50));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonThresholdState.Timeout, result.State);
    }

    [Fact]
    public async Task ThresholdAsync_WhenBodyIsNotJson_ReturnsInvalidResponse()
    {
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, "<html>not json</html>")));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonThresholdState.InvalidResponse, result.State);
    }

    [Fact]
    public async Task ThresholdAsync_WhenRequiredFieldsAreMissing_ReturnsInvalidResponse()
    {
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, """{"width": 1}""")));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonThresholdState.InvalidResponse, result.State);
    }

    [Fact]
    public async Task ThresholdAsync_WhenHttpStatusIsUnmappedError_ReturnsHttpError()
    {
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.InternalServerError, "")));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonThresholdState.HttpError, result.State);
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
