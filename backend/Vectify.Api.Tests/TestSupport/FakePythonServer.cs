using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Vectify.Api.Tests.TestSupport;

/// <summary>
/// Servidor HTTP real y mínimo que simula al motor Python/FastAPI para las pruebas
/// de integración: escucha en un puerto libre de loopback y responde /health con
/// el cuerpo, código y demora que la prueba necesite. Permite ejercitar el cliente
/// real de ASP.NET Core (IPythonVectorizationClient) contra una llamada HTTP real,
/// en vez de mockear el HttpMessageHandler.
/// </summary>
public sealed class FakePythonServer : IAsyncDisposable
{
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _acceptLoop;

    public string BaseUrl { get; }

    private FakePythonServer(HttpListener listener, string baseUrl, string responseBody, int statusCode, TimeSpan? delay)
    {
        _listener = listener;
        BaseUrl = baseUrl;
        _acceptLoop = AcceptLoopAsync(responseBody, statusCode, delay);
    }

    public static FakePythonServer Start(string responseBody, int statusCode = 200, TimeSpan? delay = null)
    {
        var port = GetFreeTcpPort();
        var baseUrl = $"http://127.0.0.1:{port}/";
        var listener = new HttpListener();
        listener.Prefixes.Add(baseUrl);
        listener.Start();
        return new FakePythonServer(listener, baseUrl, responseBody, statusCode, delay);
    }

    private async Task AcceptLoopAsync(string responseBody, int statusCode, TimeSpan? delay)
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync().WaitAsync(_cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (delay is { } d)
                {
                    try
                    {
                        await Task.Delay(d, _cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        // El servidor se está apagando mientras "dormía": cerramos igual.
                    }
                }

                context.Response.StatusCode = statusCode;
                context.Response.ContentType = "application/json";
                var bytes = Encoding.UTF8.GetBytes(responseBody);
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.OutputStream.Close();
            }
        }
        catch (ObjectDisposedException)
        {
            // El listener se cerró mientras esperaba una conexión: fin esperado.
        }
        catch (HttpListenerException)
        {
            // Idem, en algunas plataformas se manifiesta como HttpListenerException.
        }
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener.Stop();
        _listener.Close();
        try
        {
            await _acceptLoop.WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // best-effort cleanup
        }
        _cts.Dispose();
    }
}
