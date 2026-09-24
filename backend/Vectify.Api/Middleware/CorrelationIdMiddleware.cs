namespace Vectify.Api.Middleware;

/// <summary>
/// Asegura que toda request tenga un correlation/request ID: reutiliza el header
/// entrante X-Correlation-Id si viene del cliente, o genera uno nuevo. Lo agrega
/// a la respuesta y lo mete en el scope del logger para que todo log estructurado
/// emitido durante la request quede correlacionado.
/// </summary>
public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-Id";

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ILogger<CorrelationIdMiddleware> logger)
    {
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var incoming)
            && !string.IsNullOrWhiteSpace(incoming)
                ? incoming.ToString()
                : Guid.NewGuid().ToString("n");

        context.Items[HeaderName] = correlationId;

        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            logger.LogInformation(
                "Request iniciada {Method} {Path}",
                context.Request.Method,
                context.Request.Path);

            await _next(context);

            logger.LogInformation(
                "Request finalizada {Method} {Path} -> {StatusCode}",
                context.Request.Method,
                context.Request.Path,
                context.Response.StatusCode);
        }
    }
}
