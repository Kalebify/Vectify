using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Vectify.Api.Tests.TestSupport;

/// <summary>
/// Servidor Kestrel con respuestas simuladas de POST /api/v1/preprocess y
/// POST /api/v1/threshold; no ejecuta el motor Python real ni OpenCV.
/// `respond`/`respondThreshold`/`respondVectorize`/`respondSimplify` reciben el número de
/// solicitud (1-based, por endpoint) y devuelven (statusCode, body JSON), lo
/// que permite probar el cache de la Web API (una segunda solicitud con los
/// mismos parámetros no debería llegar acá). `respondThreshold`/
/// `respondVectorize`/`respondSimplify` son opcionales (default: éxito genérico) para no romper
/// los tests de sprints anteriores que solo ejercitan preprocess/threshold/vectorize.
/// </summary>
public sealed class FakePythonPreprocessServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private int _requestCount;
    private int _thresholdRequestCount;
    private int _vectorizeRequestCount;
    private int _simplifyRequestCount;

    public string BaseUrl { get; private set; } = string.Empty;
    public int RequestCount => _requestCount;
    public int ThresholdRequestCount => _thresholdRequestCount;
    public int VectorizeRequestCount => _vectorizeRequestCount;
    public int SimplifyRequestCount => _simplifyRequestCount;

    private FakePythonPreprocessServer(WebApplication app)
    {
        _app = app;
    }

    public static async Task<FakePythonPreprocessServer> StartAsync(
        Func<int, (int StatusCode, string Body)> respond,
        Func<int, (int StatusCode, string Body)>? respondThreshold = null,
        Func<int, (int StatusCode, string Body)>? respondVectorize = null,
        Func<int, (int StatusCode, string Body)>? respondSimplify = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        var app = builder.Build();

        var server = new FakePythonPreprocessServer(app);

        app.MapPost("/api/v1/preprocess", async context =>
        {
            var count = Interlocked.Increment(ref server._requestCount);
            var (statusCode, body) = respond(count);
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(body, context.RequestAborted);
        });

        app.MapPost("/api/v1/threshold", async context =>
        {
            var count = Interlocked.Increment(ref server._thresholdRequestCount);
            var (statusCode, body) = (respondThreshold ?? (_ => (200, ThresholdPayloads.SuccessBody())))(count);
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(body, context.RequestAborted);
        });

        app.MapPost("/api/v1/vectorize", async context =>
        {
            var count = Interlocked.Increment(ref server._vectorizeRequestCount);
            var (statusCode, body) = (respondVectorize ?? (_ => (200, VectorizePayloads.SuccessBody())))(count);
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(body, context.RequestAborted);
        });

        app.MapPost("/api/v1/simplify", async context =>
        {
            var count = Interlocked.Increment(ref server._simplifyRequestCount);
            var (statusCode, body) = (respondSimplify ?? (_ => (200, SimplifyPayloads.SuccessBody())))(count);
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(body, context.RequestAborted);
        });

        try
        {
            await app.StartAsync();
            var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!;
            server.BaseUrl = addresses.Addresses.Single();
            return server;
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await _app.StopAsync(timeout.Token);
        }
        finally
        {
            await _app.DisposeAsync();
        }
    }
}
