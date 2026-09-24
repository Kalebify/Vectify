using System.Collections.Concurrent;
using System.Text;
using Vectify.Api.Clients;
using Vectify.Api.Contracts;
using Vectify.Api.Storage;
using Vectify.Api.Threshold;

namespace Vectify.Api.Vectorization;

/// <summary>
/// Implementación de <see cref="IVectorizationService"/>: valida parámetros,
/// localiza la máscara B/N YA generada de origen (a través de
/// <see cref="IThresholdService"/> -- la vectorización es la etapa siguiente
/// del mismo pipeline, nunca opera sobre el preview preprocesado ni el
/// original), busca un SVG cacheado para esos parámetros exactos y, si no
/// existe, orquesta la llamada a Python sobre los bytes de la máscara,
/// versiona la configuración y la registra. Mismo patrón arquitectónico que
/// ThresholdService (M1-S04), aplicado a esta etapa -- incluye desde el
/// principio el patrón de caché+versionado+lock corregido en la ronda de fix
/// de M1-S03 (cache-hit crea versión nueva sin retroceder; lock por clave
/// para que requests concurrentes con la misma máscara de origen no dupliquen
/// la llamada a Python).
/// </summary>
public sealed class VectorizationService : IVectorizationService
{
    private readonly IThresholdService _thresholdService;
    private readonly IVectorVersionRegistry _vectorRegistry;
    private readonly IVectorParameterValidator _parameterValidator;
    private readonly IPythonVectorizeClient _pythonClient;
    private readonly IFileStorage _fileStorage;
    private readonly ILogger<VectorizationService> _logger;

    // Lock asíncrono por clave (proyecto, imagen, máscara de origen,
    // parámetros efectivos): mismo criterio que ThresholdService -- evita que
    // dos requests concurrentes con exactamente la misma combinación pasen
    // ambas el chequeo de caché como "miss" y dupliquen la llamada a Python.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _generationLocks = new();

    public VectorizationService(
        IThresholdService thresholdService,
        IVectorVersionRegistry vectorRegistry,
        IVectorParameterValidator parameterValidator,
        IPythonVectorizeClient pythonClient,
        IFileStorage fileStorage,
        ILogger<VectorizationService> logger)
    {
        _thresholdService = thresholdService;
        _vectorRegistry = vectorRegistry;
        _parameterValidator = parameterValidator;
        _pythonClient = pythonClient;
        _fileStorage = fileStorage;
        _logger = logger;
    }

    public async Task<VectorResult> GenerateVectorAsync(
        Guid projectId, Guid imageId, VectorizeRequest request, CancellationToken cancellationToken)
    {
        var validation = _parameterValidator.Validate(request);
        if (!validation.IsValid)
        {
            return new VectorResult.ValidationFailed(validation.ErrorCode!, validation.ErrorMessage!);
        }

        var parameters = validation.Parameters!;

        var sourceMask = _thresholdService.FindMask(projectId, imageId, request.MaskId);
        if (sourceMask is null)
        {
            return new VectorResult.NotFound(
                "not_found", "No existe una máscara B/N con ese ID para esta imagen.");
        }

        // Sección crítica: buscar en caché -> generar si falta -> guardar,
        // serializada por (proyecto, imagen, máscara de origen, parámetros)
        // para que requests concurrentes con la misma combinación exacta no
        // dupliquen trabajo.
        var lockKey = $"{projectId:N}/{imageId:N}/{request.MaskId:N}/{parameters.ToCacheKey()}";
        var gate = _generationLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            // Cache: misma máscara de origen + mismos parámetros efectivos ya
            // generaron un SVG antes -> se reutilizan los bytes/archivo ya
            // generados (no se vuelve a llamar a Python ni a guardar en
            // storage), pero se registra igual como una versión NUEVA del
            // historial, para que un reintento sobre parámetros ya vistos
            // avance la versión en vez de retroceder a la vieja (idempotencia
            // razonable de reintentos: no se genera un SVG duplicado).
            var cached = _vectorRegistry.FindByParams(projectId, imageId, request.MaskId, parameters);
            if (cached is not null)
            {
                var cachedVersion = _vectorRegistry.NextVersion(projectId, imageId);
                var cachedRecord = cached with
                {
                    Version = cachedVersion,
                    CreatedAt = DateTimeOffset.UtcNow,
                };
                _vectorRegistry.Save(cachedRecord);

                _logger.LogInformation(
                    "Vector {VectorId} (v{Version}) reutilizado desde caché para {ProjectId}/{ImageId}",
                    cachedRecord.VectorId, cachedVersion, projectId, imageId);

                return new VectorResult.Ready(cachedRecord, FromCache: true);
            }

            Stream sourceContent;
            try
            {
                sourceContent = await _fileStorage.OpenReadAsync(sourceMask.MaskStorageKey, cancellationToken);
            }
            catch (FileNotFoundException ex)
            {
                _logger.LogError(
                    ex,
                    "La máscara de origen {MaskId} de {ProjectId}/{ImageId} ya no está disponible en el storage",
                    request.MaskId, projectId, imageId);
                return new VectorResult.UpstreamError(
                    "storage_failure", "La máscara de origen ya no está disponible en el storage.");
            }

            PythonVectorizeResult pythonResult;
            await using (sourceContent)
            {
                pythonResult = await _pythonClient.VectorizeAsync(
                    sourceContent, sourceMask.ContentType, "mask.png", cancellationToken);
            }

            if (pythonResult.State != PythonVectorizeState.Success)
            {
                return new VectorResult.UpstreamError(
                    MapErrorCode(pythonResult.State),
                    pythonResult.Message ?? "No se pudo vectorizar la máscara.");
            }

            var vectorId = Guid.NewGuid();
            var storageKey = $"{projectId:N}/{imageId:N}/vectors/{vectorId:N}.svg";

            try
            {
                var svgBytes = Encoding.UTF8.GetBytes(pythonResult.Svg!);
                await using var svgContent = new MemoryStream(svgBytes);
                await _fileStorage.SaveAsync(storageKey, svgContent, pythonResult.ContentType!, cancellationToken);
            }
            catch (FileStorageException ex)
            {
                _logger.LogError(ex, "Fallo de storage al guardar el SVG {ProjectId}/{ImageId}", projectId, imageId);
                return new VectorResult.UpstreamError(
                    "storage_failure", "No se pudo guardar el SVG generado.");
            }

            var version = _vectorRegistry.NextVersion(projectId, imageId);
            var record = new VectorVersion(
                projectId,
                imageId,
                version,
                vectorId,
                request.MaskId,
                parameters,
                storageKey,
                pythonResult.ContentType!,
                pythonResult.Width!.Value,
                pythonResult.Height!.Value,
                pythonResult.Metrics!,
                DateTimeOffset.UtcNow);

            _vectorRegistry.Save(record);

            _logger.LogInformation(
                "Vector {VectorId} (v{Version}) generado para {ProjectId}/{ImageId} ({PathCount} paths)",
                vectorId, version, projectId, imageId, record.Metrics.PathCount);

            return new VectorResult.Ready(record, FromCache: false);
        }
        finally
        {
            gate.Release();
        }
    }

    public VectorVersion? FindVector(Guid projectId, Guid imageId, Guid vectorId) =>
        _vectorRegistry.FindByVectorId(projectId, imageId, vectorId);

    private static string MapErrorCode(PythonVectorizeState state) => state switch
    {
        PythonVectorizeState.CorruptImage => "corrupt_file",
        PythonVectorizeState.DimensionsExceeded => "dimensions_exceeded",
        PythonVectorizeState.EmptyMask => "empty_mask",
        PythonVectorizeState.InvalidParameters => "invalid_parameters",
        PythonVectorizeState.Timeout => "timeout",
        PythonVectorizeState.EngineError => "processing_error",
        PythonVectorizeState.Unavailable => "engine_unavailable",
        PythonVectorizeState.InvalidResponse => "invalid_response",
        _ => "processing_error",
    };
}
