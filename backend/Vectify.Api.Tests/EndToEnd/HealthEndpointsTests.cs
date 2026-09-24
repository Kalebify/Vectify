using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Vectify.Api.Contracts;
using Vectify.Api.Tests.TestSupport;

namespace Vectify.Api.Tests.EndToEnd;

/// <summary>
/// Pruebas de integración HTTP con motor simulado: levantan la Web API real
/// (WebApplicationFactory) y, para el motor Python, o bien un servidor HTTP real
/// (FakePythonServer) o un puerto sin listener. El PythonVectorizationClient real
/// hace la llamada de red real; nada de esto está mockeado a nivel de handler.
/// Esto verifica HTTP y deserialización frente a un servidor simulado.
/// La integración con FastAPI real se verifica por separado en tests/e2e.
/// </summary>
public sealed class HealthEndpointsTests
{
    [Fact]
    public async Task GetHealth_AlwaysReturnsOk_RegardlessOfPythonState()
    {
        await using var factory = CreateFactory(pythonBaseUrl: "http://127.0.0.1:1");
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetSystemHealth_WhenPythonRespondsOk_ReturnsOnlineViaRealCall()
    {
        await using var python = await FakePythonServer.StartAsync(
            """{"status":"ok","service":"vectify-python-engine","version":"0.1.0"}""");
        await using var factory = CreateFactory(python.BaseUrl);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/system/health");
        var body = await response.Content.ReadFromJsonAsync<SystemHealthResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal("online", body!.Status);
        Assert.Equal("online", body.Api.Status);
        Assert.Equal("online", body.Python.Status);
        Assert.Equal("vectify-python-engine", body.Python.Service);
        Assert.Equal("0.1.0", body.Python.Version);
    }

    [Fact]
    public async Task GetSystemHealth_WhenPythonIsOffline_ReturnsDegradedWithoutBreakingApi()
    {
        // Puerto de loopback sin listener: la conexión se rechaza -> Python "unavailable".
        await using var factory = CreateFactory(pythonBaseUrl: "http://127.0.0.1:1");
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/system/health");
        var body = await response.Content.ReadFromJsonAsync<SystemHealthResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // ASP.NET nunca deja de responder
        Assert.Equal("degraded", body!.Status);
        Assert.Equal("online", body.Api.Status);
        Assert.Equal("unavailable", body.Python.Status);
    }

    [Fact]
    public async Task GetSystemHealth_WhenPythonTimesOut_ReturnsDegradedWithTimeoutStatus()
    {
        await using var python = await FakePythonServer.StartAsync(
            """{"status":"ok","service":"vectify-python-engine","version":"0.1.0"}""",
            delay: TimeSpan.FromSeconds(3));
        await using var factory = CreateFactory(python.BaseUrl, timeoutSeconds: 1);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/system/health");
        var body = await response.Content.ReadFromJsonAsync<SystemHealthResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("degraded", body!.Status);
        Assert.Equal("timeout", body.Python.Status);
    }

    [Fact]
    public async Task GetSystemHealth_WhenPythonReturnsInvalidBody_ReturnsDegradedWithInvalidResponseStatus()
    {
        await using var python = await FakePythonServer.StartAsync("esto no es JSON valido");
        await using var factory = CreateFactory(python.BaseUrl);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/system/health");
        var body = await response.Content.ReadFromJsonAsync<SystemHealthResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("degraded", body!.Status);
        Assert.Equal("invalid_response", body.Python.Status);
    }

    [Fact]
    public async Task GetSystemHealth_FromAllowedOrigin_IncludesCorsHeaders()
    {
        await using var factory = CreateFactory(pythonBaseUrl: "http://127.0.0.1:1");
        var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/system/health");
        request.Headers.Add("Origin", AllowedOrigin);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Origin", out var allowOrigin));
        Assert.Equal(AllowedOrigin, Assert.Single(allowOrigin!));
        Assert.True(response.Headers.TryGetValues("Access-Control-Expose-Headers", out var exposed));
        Assert.Contains("X-Correlation-Id", string.Join(",", exposed!));
    }

    [Fact]
    public async Task GetSystemHealth_FromUnknownOrigin_OmitsCorsHeaders()
    {
        await using var factory = CreateFactory(pythonBaseUrl: "http://127.0.0.1:1");
        var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/system/health");
        request.Headers.Add("Origin", "http://evil.example");
        var response = await client.SendAsync(request);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Preflight_FromAllowedOrigin_IsAccepted()
    {
        await using var factory = CreateFactory(pythonBaseUrl: "http://127.0.0.1:1");
        var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/system/health");
        request.Headers.Add("Origin", AllowedOrigin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "x-correlation-id");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Origin", out var allowOrigin));
        Assert.Equal(AllowedOrigin, Assert.Single(allowOrigin!));
    }

    private const string AllowedOrigin = "http://localhost:5173";

    private static WebApplicationFactory<Program> CreateFactory(string pythonBaseUrl, int timeoutSeconds = 5)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["PythonEngine:BaseUrl"] = pythonBaseUrl,
                    ["PythonEngine:TimeoutSeconds"] = timeoutSeconds.ToString(),
                    ["Cors:AllowedOrigins"] = AllowedOrigin,
                });
            });
        });
    }
}
