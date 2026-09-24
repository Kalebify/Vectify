using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Vectify.Api.Clients;
using Vectify.Api.Preprocessing;
using Vectify.Api.Tests.TestSupport;

namespace Vectify.Api.Tests.Clients;

/// <summary>
/// Pruebas unitarias de PythonPreprocessClient contra un HttpMessageHandler
/// stub (sin red real, sin OpenCV): cubren los estados exigidos por el
/// contrato -- éxito, imagen corrupta, dimensiones excedidas, parámetros
/// inválidos, timeout, no disponible, respuesta inválida y error HTTP
/// genérico -- sin excepciones sin controlar escapando del cliente.
/// </summary>
public sealed class PythonPreprocessClientTests
{
    private static readonly PreprocessParameters SampleParameters = new(true, 1.5, 10, 3);

    private static PythonPreprocessClient CreateClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handlerFunc,
        TimeSpan? timeout = null)
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(handlerFunc))
        {
            BaseAddress = new Uri("http://python-engine.test"),
            Timeout = timeout ?? TimeSpan.FromSeconds(5),
        };

        return new PythonPreprocessClient(httpClient, NullLogger<PythonPreprocessClient>.Instance);
    }

    private static Task<PythonPreprocessResult> InvokeAsync(PythonPreprocessClient client) =>
        client.PreprocessAsync(new MemoryStream([1, 2, 3]), "image/png", "logo.png", SampleParameters);

    [Fact]
    public async Task PreprocessAsync_WhenResponseIsValid_ReturnsSuccessWithDecodedImage()
    {
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            PreprocessPayloads.SuccessBody(grayscale: true, contrast: 1.5, brightness: 10, denoise: 3, width: 32, height: 16))));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonPreprocessState.Success, result.State);
        Assert.NotNull(result.ImageBytes);
        Assert.NotEmpty(result.ImageBytes!);
        Assert.Equal("image/png", result.ContentType);
        Assert.Equal(32, result.Width);
        Assert.Equal(16, result.Height);
        Assert.Equal(new PreprocessParameters(true, 1.5, 10, 3), result.EffectiveParameters);
        Assert.NotNull(result.Metrics);
    }

    [Fact]
    public async Task PreprocessAsync_WhenPythonReturnsCorruptImage_ReturnsCorruptImageState()
    {
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.BadRequest, PreprocessPayloads.ErrorBody("corrupt_image", "no se pudo decodificar"))));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonPreprocessState.CorruptImage, result.State);
    }

    [Fact]
    public async Task PreprocessAsync_WhenPythonReturnsDimensionsExceeded_ReturnsDimensionsExceededState()
    {
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(
            (HttpStatusCode)413, PreprocessPayloads.ErrorBody("dimensions_exceeded", "demasiado grande"))));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonPreprocessState.DimensionsExceeded, result.State);
    }

    [Fact]
    public async Task PreprocessAsync_WhenPythonReturnsInvalidParameters_ReturnsInvalidParametersState()
    {
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(
            (HttpStatusCode)422, PreprocessPayloads.ErrorBody("invalid_parameters", "fuera de rango"))));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonPreprocessState.InvalidParameters, result.State);
    }

    [Fact]
    public async Task PreprocessAsync_WhenConnectionRefused_ReturnsUnavailable()
    {
        var client = CreateClient((_, _) => throw new HttpRequestException("Connection refused"));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonPreprocessState.Unavailable, result.State);
    }

    [Fact]
    public async Task PreprocessAsync_WhenRequestExceedsTimeout_ReturnsTimeout()
    {
        var client = CreateClient(
            async (_, ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
                return JsonResponse(HttpStatusCode.OK, PreprocessPayloads.SuccessBody());
            },
            timeout: TimeSpan.FromMilliseconds(50));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonPreprocessState.Timeout, result.State);
    }

    [Fact]
    public async Task PreprocessAsync_WhenBodyIsNotJson_ReturnsInvalidResponse()
    {
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, "<html>not json</html>")));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonPreprocessState.InvalidResponse, result.State);
    }

    [Fact]
    public async Task PreprocessAsync_WhenRequiredFieldsAreMissing_ReturnsInvalidResponse()
    {
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, """{"width": 1}""")));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonPreprocessState.InvalidResponse, result.State);
    }

    [Fact]
    public async Task PreprocessAsync_WhenHttpStatusIsUnmappedError_ReturnsHttpError()
    {
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.InternalServerError, "")));

        var result = await InvokeAsync(client);

        Assert.Equal(PythonPreprocessState.HttpError, result.State);
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
