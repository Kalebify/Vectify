using Microsoft.Extensions.Options;
using Vectify.Api.Clients;
using Vectify.Api.Contracts;
using Vectify.Api.Endpoints;
using Vectify.Api.Middleware;
using Vectify.Api.Options;
using Vectify.Api.Preprocessing;
using Vectify.Api.Projects;
using Vectify.Api.Storage;
using Vectify.Api.Threshold;
using Vectify.Api.Validation;

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

// Carga y almacenamiento de imágenes (M1-S02): validación configurable (Upload:*),
// almacenamiento local para desarrollo (Storage:*, detrás de la abstracción
// IFileStorage para poder sustituirla por S3-compatible sin tocar el endpoint) y
// un registro de proyectos en memoria (todavía no hay base de datos de negocio).
builder.Services
    .AddOptions<UploadOptions>()
    .Bind(builder.Configuration.GetSection(UploadOptions.SectionName))
    .Validate(o => o.MaxFileSizeBytes > 0, "Upload:MaxFileSizeBytes debe ser mayor a 0.")
    .Validate(o => o.GetAllowedContentTypes().Length > 0, "Upload:AllowedContentTypes no puede estar vacío.");

builder.Services
    .AddOptions<LocalStorageOptions>()
    .Bind(builder.Configuration.GetSection(LocalStorageOptions.SectionName));

// El registro de proyectos persiste un sidecar JSON por proyecto junto al
// almacenamiento local (Defecto 4 de QA sobre M1-S02: antes era puramente en
// memoria y se perdía todo al reiniciar el proceso). Sigue sin haber una base de
// datos de negocio real -- eso sigue fuera de alcance de este sprint.
builder.Services
    .AddOptions<ProjectRegistryOptions>()
    .Bind(builder.Configuration.GetSection(ProjectRegistryOptions.SectionName));

builder.Services.AddSingleton<IImageUploadValidator, ImageUploadValidator>();
builder.Services.AddSingleton<IFileStorage, LocalFileStorage>();
builder.Services.AddSingleton<IProjectRegistry, PersistentProjectRegistry>();
builder.Services.AddScoped<IProjectUploadService, ProjectUploadService>();

// Preprocesamiento de imagen (M1-S03): rangos de sliders configurables
// (Preprocess:*), un cliente Python dedicado con su propio timeout (más alto
// que el del chequeo de salud, porque OpenCV puede tardar más que un GET
// /health) y un historial de configuraciones/preview en memoria (mismo
// criterio que InMemoryProjectRegistry: sin base de datos de negocio todavía).
builder.Services
    .AddOptions<PreprocessOptions>()
    .Bind(builder.Configuration.GetSection(PreprocessOptions.SectionName))
    .Validate(o => o.MinContrast > 0 && o.MinContrast < o.MaxContrast, "Preprocess:MinContrast/MaxContrast inválidos.")
    .Validate(o => o.MinBrightness < o.MaxBrightness, "Preprocess:MinBrightness/MaxBrightness inválidos.")
    .Validate(o => o.MinDenoise >= 0 && o.MinDenoise < o.MaxDenoise, "Preprocess:MinDenoise/MaxDenoise inválidos.")
    .Validate(o => o.TimeoutSeconds > 0, "Preprocess:TimeoutSeconds debe ser mayor a 0.");

builder.Services.AddHttpClient<IPythonPreprocessClient, PythonPreprocessClient>((sp, client) =>
{
    var pythonOptions = sp.GetRequiredService<IOptions<PythonEngineOptions>>().Value;
    var preprocessOptions = sp.GetRequiredService<IOptions<PreprocessOptions>>().Value;
    client.BaseAddress = new Uri(pythonOptions.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(preprocessOptions.TimeoutSeconds);
});

builder.Services.AddSingleton<IPreprocessConfigRegistry, InMemoryPreprocessConfigRegistry>();
builder.Services.AddSingleton<IPreprocessParameterValidator, PreprocessParameterValidator>();
builder.Services.AddScoped<IPreprocessService, PreprocessService>();

// Threshold B/N (M1-S04): etapa siguiente del pipeline, opera sobre el
// preview YA preprocesado (nunca el original). Rango de umbral y umbrales de
// advertencia "casi vacía/casi llena" configurables (Threshold:*), cliente
// Python dedicado con su propio timeout y un historial de
// configuraciones/máscara en memoria (mismo criterio que preprocesamiento).
builder.Services
    .AddOptions<ThresholdOptions>()
    .Bind(builder.Configuration.GetSection(ThresholdOptions.SectionName))
    .Validate(o => o.MinValue >= 0 && o.MinValue < o.MaxValue, "Threshold:MinValue/MaxValue inválidos.")
    .Validate(
        o => o.NearEmptyMaxForegroundPercent >= 0 && o.NearEmptyMaxForegroundPercent < o.NearFullMinForegroundPercent && o.NearFullMinForegroundPercent <= 100,
        "Threshold:NearEmptyMaxForegroundPercent/NearFullMinForegroundPercent inválidos.")
    .Validate(o => o.TimeoutSeconds > 0, "Threshold:TimeoutSeconds debe ser mayor a 0.");

builder.Services.AddHttpClient<IPythonThresholdClient, PythonThresholdClient>((sp, client) =>
{
    var pythonOptions = sp.GetRequiredService<IOptions<PythonEngineOptions>>().Value;
    var thresholdOptions = sp.GetRequiredService<IOptions<ThresholdOptions>>().Value;
    client.BaseAddress = new Uri(pythonOptions.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(thresholdOptions.TimeoutSeconds);
});

builder.Services.AddSingleton<IThresholdConfigRegistry, InMemoryThresholdConfigRegistry>();
builder.Services.AddSingleton<IThresholdParameterValidator, ThresholdParameterValidator>();
builder.Services.AddScoped<IThresholdService, ThresholdService>();

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

app.MapProjectEndpoints();
app.MapPreprocessEndpoints();
app.MapThresholdEndpoints();

app.Run();

// Necesario para que WebApplicationFactory<Program> (tests de integración) pueda
// referenciar este entry point de top-level statements.
public partial class Program;
