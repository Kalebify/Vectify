using Microsoft.Extensions.Options;
using Vectify.Api.Checking;
using Vectify.Api.Clients;
using Vectify.Api.ColorPalette;
using Vectify.Api.Components;
using Vectify.Api.Contracts;
using Vectify.Api.Dimensioning;
using Vectify.Api.Endpoints;
using Vectify.Api.Export;
using Vectify.Api.Middleware;
using Vectify.Api.Options;
using Vectify.Api.PhysicalUnion;
using Vectify.Api.Preprocessing;
using Vectify.Api.Projects;
using Vectify.Api.Simplification;
using Vectify.Api.Storage;
using Vectify.Api.Threshold;
using Vectify.Api.Validation;
using Vectify.Api.VectorLayers;
using Vectify.Api.Vectorization;

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
// configuraciones/máscara persistido en disco (Defecto 2 de QA sobre
// M1-S05/M1-S06: antes era puramente en memoria y se perdía al reiniciar).
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

builder.Services
    .AddOptions<ThresholdRegistryOptions>()
    .Bind(builder.Configuration.GetSection(ThresholdRegistryOptions.SectionName));

builder.Services.AddSingleton<IThresholdConfigRegistry, PersistentThresholdConfigRegistry>();
builder.Services.AddSingleton<IThresholdParameterValidator, ThresholdParameterValidator>();
builder.Services.AddScoped<IThresholdService, ThresholdService>();

// Vectorización raster -> SVG (M1-S05): etapa siguiente del pipeline, opera
// sobre la máscara B/N YA generada por threshold (nunca el preview
// preprocesado ni el original). Sin parámetros ajustables en este sprint;
// cliente Python dedicado con su propio timeout (Vectorize:TimeoutSeconds,
// mayor al presupuesto interno de Python para que el timeout tipado de
// Python llegue primero) y un historial de VectorVersion persistido en disco
// (mismo criterio que threshold, Defecto 2 de QA sobre M1-S05/M1-S06).
builder.Services
    .AddOptions<VectorizeOptions>()
    .Bind(builder.Configuration.GetSection(VectorizeOptions.SectionName))
    .Validate(o => o.TimeoutSeconds > 0, "Vectorize:TimeoutSeconds debe ser mayor a 0.");

builder.Services.AddHttpClient<IPythonVectorizeClient, PythonVectorizeClient>((sp, client) =>
{
    var pythonOptions = sp.GetRequiredService<IOptions<PythonEngineOptions>>().Value;
    var vectorizeOptions = sp.GetRequiredService<IOptions<VectorizeOptions>>().Value;
    client.BaseAddress = new Uri(pythonOptions.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(vectorizeOptions.TimeoutSeconds);
});

builder.Services
    .AddOptions<VectorRegistryOptions>()
    .Bind(builder.Configuration.GetSection(VectorRegistryOptions.SectionName));

builder.Services.AddSingleton<IVectorVersionRegistry, PersistentVectorVersionRegistry>();
builder.Services.AddSingleton<IVectorParameterValidator, VectorParameterValidator>();
builder.Services.AddScoped<IVectorizationService, VectorizationService>();

// Simplificación de nodos (M1-S07): etapa POSTERIOR del pipeline, opera sobre
// un SVG YA vectorizado (nunca la máscara B/N ni el original). Presets
// Bajo/Medio/Alto (y una tolerancia numérica custom) configurables
// (Simplification:*), cliente Python dedicado con su propio timeout y un
// historial de SimplificationVersion persistido en disco -- mismo criterio
// que Vectorize/VectorRegistry, pero en un módulo propio y paralelo (no se
// mezcla con Vectorization, ver Vectify.Api.Simplification).
builder.Services
    .AddOptions<SimplificationOptions>()
    .Bind(builder.Configuration.GetSection(SimplificationOptions.SectionName))
    .Validate(o => o.TimeoutSeconds > 0, "Simplification:TimeoutSeconds debe ser mayor a 0.")
    .Validate(
        o => o.MinCustomTolerance >= 0 && o.MinCustomTolerance < o.MaxCustomTolerance,
        "Simplification:MinCustomTolerance/MaxCustomTolerance inválidos.")
    .Validate(
        o => o.LowEpsilonRatio > 0 && o.LowEpsilonRatio < o.MediumEpsilonRatio && o.MediumEpsilonRatio < o.HighEpsilonRatio,
        "Simplification:LowEpsilonRatio/MediumEpsilonRatio/HighEpsilonRatio deben ser crecientes y positivos.");

builder.Services.AddHttpClient<IPythonSimplifyClient, PythonSimplifyClient>((sp, client) =>
{
    var pythonOptions = sp.GetRequiredService<IOptions<PythonEngineOptions>>().Value;
    var simplificationOptions = sp.GetRequiredService<IOptions<SimplificationOptions>>().Value;
    client.BaseAddress = new Uri(pythonOptions.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(simplificationOptions.TimeoutSeconds);
});

builder.Services
    .AddOptions<SimplificationRegistryOptions>()
    .Bind(builder.Configuration.GetSection(SimplificationRegistryOptions.SectionName));

builder.Services.AddSingleton<ISimplificationVersionRegistry, PersistentSimplificationVersionRegistry>();
builder.Services.AddSingleton<ISimplificationParameterValidator, SimplificationParameterValidator>();
builder.Services.AddScoped<ISimplificationService, SimplificationService>();

// Laser Checker de paths abiertos/duplicados (M1-S08): primer análisis de
// solo lectura sobre un SVG YA generado -- una VectorVersion (M1-S05) o una
// SimplificationVersion (M1-S07), según lo que indique el request. A
// diferencia de las etapas anteriores, SIN caché/lock/registro versionado:
// nunca persiste nada, cada llamada vuelve a analizar el SVG de origen desde
// cero (determinista por construcción). Tolerancias configurables
// (Check:*), cliente Python dedicado con su propio timeout.
builder.Services
    .AddOptions<CheckOptions>()
    .Bind(builder.Configuration.GetSection(CheckOptions.SectionName))
    .Validate(o => o.TimeoutSeconds > 0, "Check:TimeoutSeconds debe ser mayor a 0.")
    .Validate(
        o => o.MinCloseGapRatio >= 0 && o.MinCloseGapRatio < o.MaxCloseGapRatio,
        "Check:MinCloseGapRatio/MaxCloseGapRatio inválidos.")
    .Validate(
        o => o.MinDuplicatePointRatio >= 0 && o.MinDuplicatePointRatio < o.MaxDuplicatePointRatio,
        "Check:MinDuplicatePointRatio/MaxDuplicatePointRatio inválidos.")
    .Validate(
        o => o.DefaultCloseGapRatio > o.MinCloseGapRatio && o.DefaultCloseGapRatio <= o.MaxCloseGapRatio,
        "Check:DefaultCloseGapRatio fuera del rango Min/MaxCloseGapRatio.")
    .Validate(
        o => o.DefaultDuplicatePointRatio > o.MinDuplicatePointRatio && o.DefaultDuplicatePointRatio <= o.MaxDuplicatePointRatio,
        "Check:DefaultDuplicatePointRatio fuera del rango Min/MaxDuplicatePointRatio.");

builder.Services.AddHttpClient<IPythonCheckClient, PythonCheckClient>((sp, client) =>
{
    var pythonOptions = sp.GetRequiredService<IOptions<PythonEngineOptions>>().Value;
    var checkOptions = sp.GetRequiredService<IOptions<CheckOptions>>().Value;
    client.BaseAddress = new Uri(pythonOptions.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(checkOptions.TimeoutSeconds);
});

builder.Services.AddSingleton<ICheckParameterValidator, CheckParameterValidator>();
builder.Services.AddScoped<ICheckService, CheckService>();

// Dimensiones físicas en mm (M1-S09): etapa que opera sobre un SVG YA
// generado -- una VectorVersion (M1-S05) o una SimplificationVersion
// (M1-S07), según lo que indique el request. A diferencia de Simplification/
// Check, NO hay cliente Python: reescribir width/height/viewBox del
// elemento raíz <svg> es pura metadata/aritmética de escala, sin ningún
// cálculo de imagen/geometría complejo que justifique un round-trip a
// FastAPI (ver Vectify.Api.Dimensioning.SvgDimensionWriter). Rango de mm
// permitido y registro de DimensionVersion persistido en disco (Dimensions:*/
// DimensionsRegistry:*), mismo criterio de caché+lock+versionado que
// Simplification/Vectorization.
builder.Services
    .AddOptions<DimensionOptions>()
    .Bind(builder.Configuration.GetSection(DimensionOptions.SectionName))
    .Validate(o => o.MinMm > 0 && o.MinMm < o.MaxMm, "Dimensions:MinMm/MaxMm inválidos.");

builder.Services
    .AddOptions<DimensionRegistryOptions>()
    .Bind(builder.Configuration.GetSection(DimensionRegistryOptions.SectionName));

builder.Services.AddSingleton<IDimensionVersionRegistry, PersistentDimensionVersionRegistry>();
builder.Services.AddSingleton<IDimensionParameterValidator, DimensionParameterValidator>();
builder.Services.AddScoped<IDimensionService, DimensionService>();

// Detección/reducción de paleta de colores (M2-S01): PRIMERA tarjeta de
// MVP2, arranca el flujo multicapa -- opera directamente sobre la imagen
// original YA subida (M1-S02, vía IProjectRegistry), no sobre ninguna
// versión previa de otra etapa del pipeline de MVP1 (sin discriminador
// `sourceKind`, a diferencia de Dimensioning/Check). Tolerancia/número
// objetivo de colores configurables (ColorPalette:*), cliente Python
// dedicado con su propio timeout y un historial de ColorPaletteVersion
// persistido en disco -- mismo criterio de caché+lock+versionado que
// Simplification/Threshold, aplicado únicamente a la detección (la única
// llamada a Python); merge/unmerge/rename/confirm son ediciones de metadata
// puras sobre la última versión de una sesión (ver ColorPaletteService).
builder.Services
    .AddOptions<ColorPaletteOptions>()
    .Bind(builder.Configuration.GetSection(ColorPaletteOptions.SectionName))
    .Validate(o => o.TimeoutSeconds > 0, "ColorPalette:TimeoutSeconds debe ser mayor a 0.")
    .Validate(
        o => o.MinTolerance >= 0 && o.MinTolerance < o.MaxTolerance,
        "ColorPalette:MinTolerance/MaxTolerance inválidos.")
    .Validate(
        o => o.DefaultTolerance >= o.MinTolerance && o.DefaultTolerance <= o.MaxTolerance,
        "ColorPalette:DefaultTolerance fuera del rango Min/MaxTolerance.")
    .Validate(
        o => o.MinColors >= 1 && o.MinColors < o.MaxColorsUpperBound,
        "ColorPalette:MinColors/MaxColorsUpperBound inválidos.");

builder.Services.AddHttpClient<IPythonColorPaletteClient, PythonColorPaletteClient>((sp, client) =>
{
    var pythonOptions = sp.GetRequiredService<IOptions<PythonEngineOptions>>().Value;
    var colorPaletteOptions = sp.GetRequiredService<IOptions<ColorPaletteOptions>>().Value;
    client.BaseAddress = new Uri(pythonOptions.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(colorPaletteOptions.TimeoutSeconds);
});

builder.Services
    .AddOptions<ColorPaletteRegistryOptions>()
    .Bind(builder.Configuration.GetSection(ColorPaletteRegistryOptions.SectionName));

builder.Services.AddSingleton<IColorPaletteVersionRegistry, PersistentColorPaletteVersionRegistry>();
builder.Services.AddSingleton<IColorPaletteParameterValidator, ColorPaletteParameterValidator>();
builder.Services.AddScoped<IColorPaletteService, ColorPaletteService>();

// Capas vectoriales por color (M2-S02): SEGUNDA tarjeta de MVP2, consume la
// paleta CONFIRMADA de M2-S01 (IColorPaletteService.FindLatest -- sin volver
// a llamar a Python para eso) y, por cada ColorGroup, vectoriza su máscara
// de forma independiente reutilizando el motor YA EXISTENTE de M1-S05 (una
// única llamada .NET -> Python resuelve las N vectorizaciones, ver
// IPythonVectorLayerClient). Cada SVG resultante se persiste como una
// VectorVersion normal en el MISMO IVectorVersionRegistry que M1-S05 --
// reutilizando el tipo ya existente, no uno paralelo. Cliente Python
// dedicado con su propio timeout (más alto: una sola request vectoriza N
// máscaras) y un historial de VectorLayerSetVersion persistido en disco --
// mismo criterio de caché+lock+versionado que ColorPalette/Vectorization.
builder.Services
    .AddOptions<VectorLayerOptions>()
    .Bind(builder.Configuration.GetSection(VectorLayerOptions.SectionName))
    .Validate(o => o.TimeoutSeconds > 0, "VectorLayer:TimeoutSeconds debe ser mayor a 0.");

builder.Services.AddHttpClient<IPythonVectorLayerClient, PythonVectorLayerClient>((sp, client) =>
{
    var pythonOptions = sp.GetRequiredService<IOptions<PythonEngineOptions>>().Value;
    var vectorLayerOptions = sp.GetRequiredService<IOptions<VectorLayerOptions>>().Value;
    client.BaseAddress = new Uri(pythonOptions.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(vectorLayerOptions.TimeoutSeconds);
});

builder.Services
    .AddOptions<VectorLayerRegistryOptions>()
    .Bind(builder.Configuration.GetSection(VectorLayerRegistryOptions.SectionName));

builder.Services.AddSingleton<IVectorLayerSetRegistry, PersistentVectorLayerSetRegistry>();
builder.Services.AddScoped<IVectorLayerService, VectorLayerService>();

// Componentes físicos independientes por capa (M2-S03): TERCERA tarjeta de
// MVP2. La propia tarjeta no tiene una sección "ASP.NET Core" explícita en
// Notion -- se sigue el mismo patrón arquitectónico de TODO el proyecto
// (Python analiza, .NET persiste/orquesta/expone, React consume, ver
// spec.md M2-S03, "Ambigüedades detectadas"): el cálculo geométrico de
// connected components corre en Python reutilizando la infraestructura YA
// COMPARTIDA de M1-S08 (tokenizer de paths/subpaths/transform), esta capa
// fina localiza el SVG de origen -- cada capa YA ES una VectorVersion
// normal (M2-S02) -- llama a Python y persiste el resultado versionado. Sin
// tolerancias ajustables desde acá (Python aplica sus propios defaults).
// Cliente Python dedicado con su propio timeout y un historial de
// ComponentSetVersion persistido en disco, cacheado por VectorId (inmutable
// una vez generado -- no necesita ninguna otra clave de caché).
builder.Services
    .AddOptions<ComponentOptions>()
    .Bind(builder.Configuration.GetSection(ComponentOptions.SectionName))
    .Validate(o => o.TimeoutSeconds > 0, "Component:TimeoutSeconds debe ser mayor a 0.");

builder.Services.AddHttpClient<IPythonComponentClient, PythonComponentClient>((sp, client) =>
{
    var pythonOptions = sp.GetRequiredService<IOptions<PythonEngineOptions>>().Value;
    var componentOptions = sp.GetRequiredService<IOptions<ComponentOptions>>().Value;
    client.BaseAddress = new Uri(pythonOptions.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(componentOptions.TimeoutSeconds);
});

builder.Services
    .AddOptions<ComponentRegistryOptions>()
    .Bind(builder.Configuration.GetSection(ComponentRegistryOptions.SectionName));

builder.Services.AddSingleton<IComponentVersionRegistry, PersistentComponentVersionRegistry>();
builder.Services.AddScoped<IComponentAnalysisService, ComponentAnalysisService>();

// Agrupación LÓGICA de componentes (M2-S05): CUARTA tarjeta de MVP2 sobre el
// módulo de componentes. A diferencia de M2-S03, NO hay ninguna llamada a
// Python ni a storage: agrupar/desagrupar/renombrar son ediciones de
// metadata puras que referencian componentIds YA calculados (vía
// IComponentAnalysisService.FindLatest, solo lectura), nunca tocan paths ni
// crean una nueva VectorVersion/ComponentSetVersion. Mismo patrón de
// versionado inmutable (nunca mutar una versión existente) que el resto del
// pipeline, persistido en disco con el mismo criterio exacto que
// PersistentComponentVersionRegistry (sidecar por VectorId).
builder.Services
    .AddOptions<ComponentGroupRegistryOptions>()
    .Bind(builder.Configuration.GetSection(ComponentGroupRegistryOptions.SectionName));

builder.Services.AddSingleton<IComponentGroupVersionRegistry, PersistentComponentGroupVersionRegistry>();
builder.Services.AddScoped<IComponentGroupService, ComponentGroupService>();

// Unión física de piezas (M2-S06): SEXTA tarjeta de MVP2 sobre el módulo de
// componentes -- A DIFERENCIA de M2-S05 (agrupar, lógico, nunca toca
// geometría), esta SÍ modifica geometría real: fusiona 2+ componentes
// físicos ya calculados (M2-S03) en una única pieza fabricable (unión
// booleana para piezas solapadas/tangentes, bridge simple/directo para
// piezas separadas -- ver services/python-engine/app/core/physical_union.py,
// que usa Shapely, ver justificación en IMPL.md del sprint). Preview
// calcula la geometría real SIN persistir nada; confirm persiste el
// resultado como una VectorVersion NUEVA en el MISMO IVectorVersionRegistry
// que el resto del pipeline -- reutilizando el tipo ya existente, no uno
// paralelo -- más un PhysicalUnionVersion de auditoría versionado por el
// VectorId de origen, mismo patrón que ComponentGroupSetVersion. La
// VectorVersion anterior NUNCA se destruye. Cliente Python dedicado con su
// propio timeout (más alto que Component:TimeoutSeconds: además de las
// operaciones booleanas/bridging, Python corre una segunda pasada completa
// del analizador de componentes como validación post-operación no
// negociable, ver spec.md: "nunca fingir unión").
builder.Services
    .AddOptions<PhysicalUnionOptions>()
    .Bind(builder.Configuration.GetSection(PhysicalUnionOptions.SectionName))
    .Validate(o => o.TimeoutSeconds > 0, "PhysicalUnion:TimeoutSeconds debe ser mayor a 0.");

builder.Services.AddHttpClient<IPythonPhysicalUnionClient, PythonPhysicalUnionClient>((sp, client) =>
{
    var pythonOptions = sp.GetRequiredService<IOptions<PythonEngineOptions>>().Value;
    var physicalUnionOptions = sp.GetRequiredService<IOptions<PhysicalUnionOptions>>().Value;
    client.BaseAddress = new Uri(pythonOptions.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(physicalUnionOptions.TimeoutSeconds);
});

builder.Services
    .AddOptions<PhysicalUnionRegistryOptions>()
    .Bind(builder.Configuration.GetSection(PhysicalUnionRegistryOptions.SectionName));

builder.Services.AddSingleton<IPhysicalUnionVersionRegistry, PersistentPhysicalUnionVersionRegistry>();
builder.Services.AddScoped<IPhysicalUnionService, PhysicalUnionService>();

// Exportación SVG (M1-S10): cierra el primer flujo productivo. Sirve, sin
// modificar, los mismos bytes ya persistidos por Vectorization/
// Simplification/Dimensioning -- SIN cliente Python, SIN caché/lock/registro
// versionado propio (no genera ningún artefacto nuevo que versionar). Ver
// Vectify.Api.Export.ExportService.
builder.Services.AddScoped<IExportService, ExportService>();

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
app.MapVectorizationEndpoints();
app.MapSimplificationEndpoints();
app.MapCheckEndpoints();
app.MapDimensionEndpoints();
app.MapExportEndpoints();
app.MapColorPaletteEndpoints();
app.MapVectorLayerEndpoints();
app.MapComponentEndpoints();
app.MapComponentGroupEndpoints();
app.MapPhysicalUnionEndpoints();

app.Run();

// Necesario para que WebApplicationFactory<Program> (tests de integración) pueda
// referenciar este entry point de top-level statements.
public partial class Program;
