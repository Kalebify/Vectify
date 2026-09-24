using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Vectify.Api.Tests.TestSupport;

/// <summary>Servidor Kestrel con respuestas simuladas; no ejecuta Python.</summary>
public sealed class FakePythonServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    public string BaseUrl { get; }
    private FakePythonServer(WebApplication app, string baseUrl)
    {
        _app = app;
        BaseUrl = baseUrl;
    }
    public static async Task<FakePythonServer> StartAsync(string responseBody, int statusCode = 200, TimeSpan? delay = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        var app = builder.Build();
        app.MapGet("/health", async context =>
        {
            if (delay is { } duration) await Task.Delay(duration, context.RequestAborted);
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(responseBody, context.RequestAborted);
        });
        try
        {
            await app.StartAsync();
            var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!;
            return new FakePythonServer(app, addresses.Addresses.Single());
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
        try { await _app.StopAsync(timeout.Token); }
        finally { await _app.DisposeAsync(); }
    }
}
