using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Vectify.Api.Clients;
using Vectify.Api.Contracts;
using Vectify.Api.Options;
using Vectify.Api.Preprocessing;
using Vectify.Api.Storage;

namespace Vectify.Api.Threshold;

/// <summary>
/// Implementación de <see cref="IThresholdService"/>: valida parámetros,
/// localiza el preview YA preprocesado de origen (a través de
/// <see cref="IPreprocessService"/> -- el threshold es la etapa siguiente del
/// mismo pipeline, nunca opera sobre el original crudo), busca una máscara
/// cacheada para esos parámetros exactos y, si no existe, orquesta la llamada
/// a Python sobre los bytes del preview, versiona la configuración y la
/// registra. Mismo patrón arquitectónico que PreprocessService (M1-S03),
/// aplicado a una etapa distinta del pipeline -- ver "Decisiones de diseño"
/// en el reporte del sprint.
/// </summary>
public sealed class ThresholdService : IThresholdService
{
    private readonly IPreprocessService _preprocessService;
    private readonly IThresholdConfigRegistry _thresholdRegistry;
    private readonly IThresholdParameterValidator _parameterValidator;
    private readonly IPythonThresholdClient _pythonClient;
    private readonly IFileStorage _fileStorage;
    private readonly ThresholdOptions _options;
    private readonly ILogger<ThresholdService> _logger;

    // Lock asíncrono por clave (proyecto, imagen, preview de origen, parámetros
    // efectivos): mismo criterio que PreprocessService -- evita que dos
    // requests concurrentes con exactamente la misma combinación pasen ambas
    // el chequeo de caché como "miss" y dupliquen la llamada a Python.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _generationLocks = new();

    public ThresholdService(
        IPreprocessService preprocessService,
        IThresholdConfigRegistry thresholdRegistry,
        IThresholdParameterValidator parameterValidator,
        IPythonThresholdClient pythonClient,
        IFileStorage fileStorage,
        IOptions<ThresholdOptions> options,
        ILogger<ThresholdService> logger)
    {
        _preprocessService = preprocessService;
        _thresholdRegistry = thresholdRegistry;
        _parameterValidator = parameterValidator;
        _pythonClient = pythonClient;
        _fileStorage = fileStorage;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ThresholdResult> GenerateMaskAsync(
        Guid projectId, Guid imageId, ThresholdRequest request, CancellationToken cancellationToken)
    {
        var validation = _parameterValidator.Validate(request);
        if (!validation.IsValid)
        {
            return new ThresholdResult.ValidationFailed(validation.ErrorCode!, validation.ErrorMessage!);
        }

        var parameters = validation.Parameters!;

        var sourcePreview = _preprocessService.FindPreview(projectId, imageId, request.PreviewId);
        if (sourcePreview is null)
        {
            return new ThresholdResult.NotFound(
                "not_found", "No existe un preview preprocesado con ese ID para esta imagen.");
        }

        // Sección crítica: buscar en caché -> generar si falta -> guardar,
        // serializada por (proyecto, imagen, preview de origen, parámetros)
        // para que requests concurrentes con la misma combinación exacta no
        // dupliquen trabajo (mismo criterio que PreprocessService, ya
        // corregido en la ronda anterior de M1-S03).
        var lockKey = $"{projectId:N}/{imageId:N}/{request.PreviewId:N}/{parameters.ToCacheKey()}";
        var gate = _generationLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            // Cache: mismo preview de origen + mismos parámetros efectivos ya
            // generaron una máscara antes -> se reutilizan los bytes/archivo
            // ya generados (no se vuelve a llamar a Python ni a guardar en
            // storage), pero se registra igual como una versión NUEVA del
            // historial, para que un reset a parámetros ya vistos avance la
            // versión en vez de retroceder a la vieja.
            var cached = _thresholdRegistry.FindByParams(projectId, imageId, request.PreviewId, parameters);
            if (cached is not null)
            {
                var cachedVersion = _thresholdRegistry.NextVersion(projectId, imageId);
                var cachedRecord = cached with
                {
                    Version = cachedVersion,
                    CreatedAt = DateTimeOffset.UtcNow,
                };
                _thresholdRegistry.Save(cachedRecord);

                _logger.LogInformation(
                    "Máscara {MaskId} (v{Version}) reutilizada desde caché para {ProjectId}/{ImageId}",
                    cachedRecord.MaskId, cachedVersion, projectId, imageId);

                return new ThresholdResult.Ready(cachedRecord, FromCache: true);
            }

            Stream sourceContent;
            try
            {
                sourceContent = await _fileStorage.OpenReadAsync(sourcePreview.PreviewStorageKey, cancellationToken);
            }
            catch (FileNotFoundException ex)
            {
                _logger.LogError(
                    ex,
                    "El preview de origen {PreviewId} de {ProjectId}/{ImageId} ya no está disponible en el storage",
                    request.PreviewId, projectId, imageId);
                return new ThresholdResult.UpstreamError(
                    "storage_failure", "El preview de origen ya no está disponible en el storage.");
            }

            PythonThresholdResult pythonResult;
            await using (sourceContent)
            {
                pythonResult = await _pythonClient.ThresholdAsync(
                    sourceContent, sourcePreview.ContentType, "preview.png", parameters, cancellationToken);
            }

            if (pythonResult.State != PythonThresholdState.Success)
            {
                return new ThresholdResult.UpstreamError(
                    MapErrorCode(pythonResult.State),
                    pythonResult.Message ?? "No se pudo generar la máscara.");
            }

            var maskId = Guid.NewGuid();
            var storageKey = $"{projectId:N}/{imageId:N}/masks/{maskId:N}.png";

            try
            {
                await using var maskContent = new MemoryStream(pythonResult.ImageBytes!);
                await _fileStorage.SaveAsync(storageKey, maskContent, pythonResult.ContentType!, cancellationToken);
            }
            catch (FileStorageException ex)
            {
                _logger.LogError(ex, "Fallo de storage al guardar la máscara {ProjectId}/{ImageId}", projectId, imageId);
                return new ThresholdResult.UpstreamError(
                    "storage_failure", "No se pudo guardar la máscara generada.");
            }

            var metrics = EvaluateMetrics(pythonResult.Metrics!);

            var version = _thresholdRegistry.NextVersion(projectId, imageId);
            var record = new ThresholdConfigRecord(
                projectId,
                imageId,
                version,
                maskId,
                request.PreviewId,
                pythonResult.EffectiveParameters ?? parameters,
                storageKey,
                pythonResult.ContentType!,
                pythonResult.Width!.Value,
                pythonResult.Height!.Value,
                metrics,
                DateTimeOffset.UtcNow);

            _thresholdRegistry.Save(record);

            _logger.LogInformation(
                "Máscara {MaskId} (v{Version}) generada para {ProjectId}/{ImageId} ({WarningCode})",
                maskId, version, projectId, imageId, metrics.WarningCode ?? "sin advertencia");

            return new ThresholdResult.Ready(record, FromCache: false);
        }
        finally
        {
            gate.Release();
        }
    }

    public ThresholdConfigRecord? FindMask(Guid projectId, Guid imageId, Guid maskId) =>
        _thresholdRegistry.FindByMaskId(projectId, imageId, maskId);

    /// <summary>
    /// Clasifica las métricas crudas de Python contra los umbrales
    /// configurados (Threshold:NearEmptyMaxForegroundPercent /
    /// NearFullMinForegroundPercent) y arma el código/mensaje de advertencia
    /// que consume React. Deliberadamente del lado de Vectify.Api, no de
    /// Python: Python solo calcula el porcentaje crudo (ver spec.md M1-S04,
    /// "Python/FastAPI"); qué cuenta como "casi vacía/llena" es una regla de
    /// negocio configurable, mismo criterio que los rangos de sliders de
    /// preprocesamiento (Preprocess:*).
    /// </summary>
    private ThresholdMetrics EvaluateMetrics(ThresholdRawMetrics raw)
    {
        var isNearEmpty = raw.ForegroundPercent <= _options.NearEmptyMaxForegroundPercent;
        var isNearFull = raw.ForegroundPercent >= _options.NearFullMinForegroundPercent;

        string? warningCode = null;
        string? warningMessage = null;

        if (isNearEmpty)
        {
            warningCode = "mask_near_empty";
            warningMessage =
                "La máscara resultante quedó casi vacía. Puede no ser útil para vectorizar: probá bajar el umbral o invertir.";
        }
        else if (isNearFull)
        {
            warningCode = "mask_near_full";
            warningMessage =
                "La máscara resultante quedó casi completa. Puede no ser útil para vectorizar: probá subir el umbral o invertir.";
        }

        return new ThresholdMetrics(
            raw.ForegroundPercent, raw.BackgroundPercent, isNearEmpty, isNearFull, warningCode, warningMessage);
    }

    private static string MapErrorCode(PythonThresholdState state) => state switch
    {
        PythonThresholdState.CorruptImage => "corrupt_file",
        PythonThresholdState.DimensionsExceeded => "dimensions_exceeded",
        PythonThresholdState.InvalidParameters => "invalid_parameters",
        PythonThresholdState.Timeout => "timeout",
        PythonThresholdState.Unavailable => "engine_unavailable",
        PythonThresholdState.InvalidResponse => "invalid_response",
        _ => "processing_error",
    };
}
