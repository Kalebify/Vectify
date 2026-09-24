using Microsoft.Extensions.Options;
using Vectify.Api.Clients;
using Vectify.Api.Contracts;
using Vectify.Api.Middleware;
using Vectify.Api.Options;

const string ApiVersion = "0.1.0";
const string ApiServiceName = "vectify-api";

var builder = WebApplication.CreateBuilder(args);

// Logging estructurado: formatter JSON (incluye scopes, por lo tanto el
// correlation ID agregado en CorrelationIdMiddleware queda en cada línea).
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffK ";
});

// Documentación OpenAPI/Swagger (Swashbuckle): genera el documento y sirve la UI en desarrollo.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.OpenApiInfo
    {
        Title = "Vectify API",
        Version = "v1",
        Description = "Backend/orquestador ASP.NET Core. Compone el estado de salud propio y del motor Python.",
    });
});

// Configuración tipada del motor Python, enlazada a la sección "PythonEngine"
// (appsettings.json o variables de entorno PythonEngine__BaseUrl / PythonEngine__TimeoutSeconds).
builder.Services
    .AddOptions<PythonEngineOptions>()
    .Bind(builder.Configuration.GetSection(PythonEngineOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.BaseUrl), "PythonEngine:BaseUrl es requerido.")
    .Validate(o => o.TimeoutSeconds > 0, "PythonEngine:TimeoutSeconds debe ser mayor a 0.");

// Cliente tipado hacia FastAPI vía IHttpClientFactory. BaseAddress y timeout
// salen de PythonEngineOptions, nunca hardcodeados.
builder.Services.AddHttpClient<IPythonVectorizationClient, PythonVectorizationClient>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<PythonEngineOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
});

// CORS: React se sirve desde otro origen (p. ej. http://localhost:5173) que la
// Web API (http://localhost:5080), así que sin esta política el navegador bloquearía
// la lectura de las respuestas. Los orígenes salen de configuración (sección "Cors"),
// nunca hardcodeados, y se resuelven de forma diferida (igual que PythonEngineOptions)
// para respetar overrides de entorno y de WebApplicationFactory en los tests.
// Se expone X-Correlation-Id para que el frontend pueda leerlo.
builder.Services
    .AddOptions<FrontendCorsOptions>()
    .Bind(builder.Configuration.GetSection(FrontendCorsOptions.SectionName));

builder.Services.AddCors();
builder.Services
    .AddOptions<Microsoft.AspNetCore.Cors.Infrastructure.CorsOptions>()
    .Configure<IOptions<FrontendCorsOptions>>((cors, frontend) =>
        cors.AddPolicy(FrontendCorsOptions.PolicyName, policy => policy
            .WithOrigins(frontend.Value.GetOrigins())
            .AllowAnyHeader()
            .AllowAnyMethod()
            .WithExposedHeaders(CorrelationIdMiddleware.HeaderName)));

var app = builder.Build();

var allowedOrigins = app.Services.GetRequiredService<IOptions<FrontendCorsOptions>>().Value.GetOrigins();
if (allowedOrigins.Length == 0)
{
    app.Logger.LogWarning(
        "Cors:AllowedOrigins está vacío: ningún navegador podrá leer las respuestas de la Web API.");
}
else
{
    app.Logger.LogInformation("CORS habilitado para los orígenes {AllowedOrigins}", string.Join(", ", allowedOrigins));
}

app.UseMiddleware<CorrelationIdMiddleware>();

app.UseCors(FrontendCorsOptions.PolicyName);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Vectify API v1");
        options.RoutePrefix = "swagger";
    });
}

// Liveness simple de la propia Web API: no depende de Python. Si esto responde,
// ASP.NET Core está arriba, sin importar el estado del motor.
app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    service = ApiServiceName,
    version = ApiVersion,
    timestamp = DateTimeOffset.UtcNow,
}))
.WithName("GetHealth")
.WithTags("Health");

// Estado global compuesto que consume React: siempre responde 200 (la Web API
// nunca "cae" por culpa de Python), reflejando online/degraded según corresponda.
app.MapGet("/api/v1/system/health", async (
    IPythonVectorizationClient pythonClient,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    var pythonResult = await pythonClient.CheckHealthAsync(cancellationToken);

    var pythonStatus = pythonResult.State switch
    {
        PythonHealthState.Online => "online",
        PythonHealthState.Timeout => "timeout",
        PythonHealthState.Unavailable => "unavailable",
        PythonHealthState.InvalidResponse => "invalid_response",
        PythonHealthState.HttpError => "error",
        _ => "error",
    };

    var overallStatus = pythonResult.State == PythonHealthState.Online ? "online" : "degraded";

    if (overallStatus == "degraded")
    {
        logger.LogWarning(
            "Estado global degradado: motor Python en estado {PythonStatus} ({Message})",
            pythonStatus,
            pythonResult.Message);
    }

    var response = new SystemHealthResponse(
        Status: overallStatus,
        Timestamp: DateTimeOffset.UtcNow,
        Api: new ApiHealthInfo("online"),
        Python: new PythonHealthInfo(pythonStatus, pythonResult.Service, pythonResult.Version, pythonResult.Message));

    return Results.Ok(response);
})
.WithName("GetSystemHealth")
.WithTags("Health");

app.Run();

// Necesario para que WebApplicationFactory<Program> (tests de integración) pueda
// referenciar este entry point de top-level statements.
public partial class Program;
