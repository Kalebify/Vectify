using System.Collections.Concurrent;
using System.Text;
using Vectify.Api.Clients;
using Vectify.Api.Contracts;
using Vectify.Api.Storage;
using Vectify.Api.Vectorization;

namespace Vectify.Api.Simplification;

/// <summary>
/// Implementación de <see cref="ISimplificationService"/>: valida parámetros,
/// localiza el SVG de origen YA generado (a través de
/// <see cref="IVectorizationService"/> -- la simplificación es una etapa
/// posterior del mismo pipeline, nunca opera sobre la máscara B/N ni el
/// original), y expone dos flujos distintos pedidos explícitamente por
/// spec.md M1-S07:
///
/// - <see cref="PreviewAsync"/>: SIN caché ni registro. Cada llamada invoca a
///   Python de nuevo y devuelve el SVG + métricas directamente en la
///   respuesta, sin persistir nada -- "Preview es reversible: cancelar no
///   deja rastro".
/// - <see cref="ApplyAsync"/>: mismo patrón de caché+lock+versionado que
///   VectorizationService/ThresholdService (cache-hit crea una versión NUEVA
///   sin retroceder; lock por clave para que requests concurrentes con el
///   mismo SVG de origen + parámetros no dupliquen la llamada a Python).
/// </summary>
public sealed class SimplificationService : ISimplificationService
{
    private readonly IVectorizationService _vectorizationService;
    private readonly ISimplificationVersionRegistry _simplificationRegistry;
    private readonly ISimplificationParameterValidator _parameterValidator;
    private readonly IPythonSimplifyClient _pythonClient;
    private readonly IFileStorage _fileStorage;
    private readonly ILogger<SimplificationService> _logger;

    // Lock asíncrono por clave (proyecto, imagen, SVG de origen, parámetros
    // efectivos): mismo criterio que VectorizationService/ThresholdService --
    // solo protege ApplyAsync (PreviewAsync no escribe estado compartido, no
    // necesita sección crítica).
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _generationLocks = new();

    public SimplificationService(
        IVectorizationService vectorizationService,
        ISimplificationVersionRegistry simplificationRegistry,
        ISimplificationParameterValidator parameterValidator,
        IPythonSimplifyClient pythonClient,
        IFileStorage fileStorage,
        ILogger<SimplificationService> logger)
    {
        _vectorizationService = vectorizationService;
        _simplificationRegistry = simplificationRegistry;
        _parameterValidator = parameterValidator;
        _pythonClient = pythonClient;
        _fileStorage = fileStorage;
        _logger = logger;
    }

    public async Task<SimplificationPreviewResult> PreviewAsync(
        Guid projectId, Guid imageId, SimplifyRequest request, CancellationToken cancellationToken)
    {
        var validation = _parameterValidator.Validate(request);
        if (!validation.IsValid)
        {
            return new SimplificationPreviewResult.ValidationFailed(validation.ErrorCode!, validation.ErrorMessage!);
        }

        var parameters = validation.Parameters!;

        var sourceVector = _vectorizationService.FindVector(projectId, imageId, request.VectorId);
        if (sourceVector is null)
        {
            return new SimplificationPreviewResult.NotFound(
                "not_found", "No existe un SVG vectorizado con ese ID para esta imagen.");
        }

        Stream sourceContent;
        try
        {
            sourceContent = await _fileStorage.OpenReadAsync(sourceVector.SvgStorageKey, cancellationToken);
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogError(
                ex,
                "El SVG de origen {VectorId} de {ProjectId}/{ImageId} ya no está disponible en el storage",
                request.VectorId, projectId, imageId);
            return new SimplificationPreviewResult.UpstreamError(
                "storage_failure", "El SVG de origen ya no está disponible en el storage.");
        }

        PythonSimplifyResult pythonResult;
        await using (sourceContent)
        {
            pythonResult = await _pythonClient.SimplifyAsync(
                sourceContent, sourceVector.ContentType, "vector.svg", parameters, cancellationToken);
        }

        if (pythonResult.State != PythonSimplifyState.Success)
        {
            return new SimplificationPreviewResult.UpstreamError(
                MapErrorCode(pythonResult.State),
                pythonResult.Message ?? "No se pudo generar el preview de simplificación.");
        }

        _logger.LogInformation(
            "Preview de simplificación generado para {ProjectId}/{ImageId} sobre vector {VectorId} (reducción {ReductionPercent}%, sin persistir)",
            projectId, imageId, request.VectorId, pythonResult.Metrics!.ReductionPercent);

        return new SimplificationPreviewResult.Ready(
            pythonResult.Svg!,
            pythonResult.ContentType!,
            sourceVector.Width,
            sourceVector.Height,
            pythonResult.Metrics!,
            parameters);
    }

    public async Task<SimplificationResult> ApplyAsync(
        Guid projectId, Guid imageId, SimplifyRequest request, CancellationToken cancellationToken)
    {
        var validation = _parameterValidator.Validate(request);
        if (!validation.IsValid)
        {
            return new SimplificationResult.ValidationFailed(validation.ErrorCode!, validation.ErrorMessage!);
        }

        var parameters = validation.Parameters!;

        var sourceVector = _vectorizationService.FindVector(projectId, imageId, request.VectorId);
        if (sourceVector is null)
        {
            return new SimplificationResult.NotFound(
                "not_found", "No existe un SVG vectorizado con ese ID para esta imagen.");
        }

        // Sección crítica: buscar en caché -> generar si falta -> guardar,
        // serializada por (proyecto, imagen, vector de origen, parámetros)
        // para que requests concurrentes con la misma combinación exacta no
        // dupliquen trabajo -- mismo criterio que VectorizationService.
        var lockKey = $"{projectId:N}/{imageId:N}/{request.VectorId:N}/{parameters.ToCacheKey()}";
        var gate = _generationLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            // Cache: mismo SVG de origen + mismos parámetros efectivos ya
            // generaron una simplificación antes -> se reutilizan los bytes/
            // archivo ya generados, pero se registra igual como una versión
            // NUEVA del historial (nunca se retrocede a una vieja).
            var cached = _simplificationRegistry.FindByParams(projectId, imageId, request.VectorId, parameters);
            if (cached is not null)
            {
                var cachedVersion = _simplificationRegistry.NextVersion(projectId, imageId);
                var cachedRecord = cached with
                {
                    Version = cachedVersion,
                    CreatedAt = DateTimeOffset.UtcNow,
                };
                _simplificationRegistry.Save(cachedRecord);

                _logger.LogInformation(
                    "Simplificación {SimplificationId} (v{Version}) reutilizada desde caché para {ProjectId}/{ImageId}",
                    cachedRecord.SimplificationId, cachedVersion, projectId, imageId);

                return new SimplificationResult.Ready(cachedRecord, FromCache: true);
            }

            Stream sourceContent;
            try
            {
                sourceContent = await _fileStorage.OpenReadAsync(sourceVector.SvgStorageKey, cancellationToken);
            }
            catch (FileNotFoundException ex)
            {
                _logger.LogError(
                    ex,
                    "El SVG de origen {VectorId} de {ProjectId}/{ImageId} ya no está disponible en el storage",
                    request.VectorId, projectId, imageId);
                return new SimplificationResult.UpstreamError(
                    "storage_failure", "El SVG de origen ya no está disponible en el storage.");
            }

            PythonSimplifyResult pythonResult;
            await using (sourceContent)
            {
                pythonResult = await _pythonClient.SimplifyAsync(
                    sourceContent, sourceVector.ContentType, "vector.svg", parameters, cancellationToken);
            }

            if (pythonResult.State != PythonSimplifyState.Success)
            {
                return new SimplificationResult.UpstreamError(
                    MapErrorCode(pythonResult.State),
                    pythonResult.Message ?? "No se pudo simplificar el SVG.");
            }

            var simplificationId = Guid.NewGuid();
            var storageKey = $"{projectId:N}/{imageId:N}/simplifications/{simplificationId:N}.svg";

            try
            {
                var svgBytes = Encoding.UTF8.GetBytes(pythonResult.Svg!);
                await using var svgContent = new MemoryStream(svgBytes);
                await _fileStorage.SaveAsync(storageKey, svgContent, pythonResult.ContentType!, cancellationToken);
            }
            catch (FileStorageException ex)
            {
                _logger.LogError(ex, "Fallo de storage al guardar el SVG simplificado {ProjectId}/{ImageId}", projectId, imageId);
                return new SimplificationResult.UpstreamError(
                    "storage_failure", "No se pudo guardar el SVG simplificado.");
            }

            // Ancho/alto: la simplificación nunca cambia las dimensiones del
            // lienzo (solo reduce puntos de cada `d`), así que se reutilizan
            // directamente del VectorVersion de origen en vez de pedírselos
            // de vuelta a Python -- evita un round-trip redundante.
            var version = _simplificationRegistry.NextVersion(projectId, imageId);
            var record = new SimplificationVersion(
                projectId,
                imageId,
                version,
                simplificationId,
                request.VectorId,
                parameters,
                storageKey,
                pythonResult.ContentType!,
                sourceVector.Width,
                sourceVector.Height,
                pythonResult.Metrics!,
                DateTimeOffset.UtcNow);

            _simplificationRegistry.Save(record);

            _logger.LogInformation(
                "Simplificación {SimplificationId} (v{Version}) generada para {ProjectId}/{ImageId} (reducción {ReductionPercent}%)",
                simplificationId, version, projectId, imageId, record.Metrics.ReductionPercent);

            return new SimplificationResult.Ready(record, FromCache: false);
        }
        finally
        {
            gate.Release();
        }
    }

    public SimplificationVersion? FindSimplification(Guid projectId, Guid imageId, Guid simplificationId) =>
        _simplificationRegistry.FindBySimplificationId(projectId, imageId, simplificationId);

    private static string MapErrorCode(PythonSimplifyState state) => state switch
    {
        PythonSimplifyState.InvalidInputSvg => "invalid_input_svg",
        PythonSimplifyState.SvgTooLarge => "svg_too_large",
        PythonSimplifyState.InvalidParameters => "invalid_parameters",
        PythonSimplifyState.Timeout => "timeout",
        PythonSimplifyState.EngineError => "processing_error",
        PythonSimplifyState.Unavailable => "engine_unavailable",
        PythonSimplifyState.InvalidResponse => "invalid_response",
        // Igual que InvalidResponse: la respuesta de Python no pasa la validación defensiva
        // adicional del cliente -- Vectify.Api no confía en su contenido, mismo código y
        // semántica HTTP (502) que un JSON con campos faltantes.
        PythonSimplifyState.InvalidSvg => "invalid_response",
        _ => "processing_error",
    };
}
