using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Vectify.Api.Clients;

namespace Vectify.Api.Tests.Clients;

/// <summary>
/// Pruebas unitarias de PythonVectorizationClient contra un HttpMessageHandler
/// stub (sin red real): cubren los estados exigidos por el contrato -- online,
/// no disponible, timeout, respuesta inválida y error HTTP -- sin excepciones
/// sin controlar escapando del cliente.
/// </summary>
public sealed class PythonVectorizationClientTests
{
    private static PythonVectorizationClient CreateClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handlerFunc,
        TimeSpan? timeout = null)
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(handlerFunc))
        {
            BaseAddress = new Uri("http://python-engine.test"),
            Timeout = timeout ?? TimeSpan.FromSeconds(5),
        };

        return new PythonVectorizationClient(httpClient, NullLogger<PythonVectorizationClient>.Instance);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenResponseIsValid_ReturnsOnline()
    {
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            """{"status":"ok","service":"vectify-python-engine","version":"0.1.0"}""")));

        var result = await client.CheckHealthAsync();

        Assert.Equal(PythonHealthState.Online, result.State);
        Assert.Equal("vectify-python-engine", result.Service);
        Assert.Equal("0.1.0", result.Version);
        Assert.Null(result.Message);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenConnectionRefused_ReturnsUnavailable()
    {
        var client = CreateClient((_, _) => throw new HttpRequestException("Connection refused"));

        var result = await client.CheckHealthAsync();

        Assert.Equal(PythonHealthState.Unavailable, result.State);
        Assert.NotNull(result.Message);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenRequestExceedsTimeout_ReturnsTimeout()
    {
        var client = CreateClient(
            async (_, ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
                return JsonResponse(HttpStatusCode.OK, """{"status":"ok","service":"x","version":"1"}""");
            },
            timeout: TimeSpan.FromMilliseconds(50));

        var result = await client.CheckHealthAsync();

        Assert.Equal(PythonHealthState.Timeout, result.State);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenBodyIsNotJson_ReturnsInvalidResponse()
    {
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, "<html>not json</html>")));

        var result = await client.CheckHealthAsync();

        Assert.Equal(PythonHealthState.InvalidResponse, result.State);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenRequiredFieldsAreMissing_ReturnsInvalidResponse()
    {
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, """{"status":"ok"}""")));

        var result = await client.CheckHealthAsync();

        Assert.Equal(PythonHealthState.InvalidResponse, result.State);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenHttpStatusIsError_ReturnsHttpError()
    {
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.InternalServerError, "")));

        var result = await client.CheckHealthAsync();

        Assert.Equal(PythonHealthState.HttpError, result.State);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenReportedStatusIsNotOk_ReturnsHttpError()
    {
        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            """{"status":"degraded","service":"vectify-python-engine","version":"0.1.0"}""")));

        var result = await client.CheckHealthAsync();

        Assert.Equal(PythonHealthState.HttpError, result.State);
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
