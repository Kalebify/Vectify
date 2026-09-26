using System.Collections.Concurrent;
using Vectify.Api.Clients;
using Vectify.Api.Storage;
using Vectify.Api.Vectorization;

namespace Vectify.Api.Components;

/// <summary>
/// Implementación de <see cref="IComponentAnalysisService"/>: localiza el
/// SVG de origen -- una <see cref="VectorVersion"/> YA EXISTENTE (cada capa
/// de M2-S02 ES una VectorVersion normal, ver
/// <see cref="Vectify.Api.VectorLayers.VectorLayer.VectorId"/>) -- y le pide
/// el análisis de componentes físicos al motor Python. Mismo patrón de
/// cache+lock+versionado que DimensionService/VectorLayerService: cache-hit
/// (mismo VectorId, que es inmutable, ya visto) crea una versión NUEVA del
/// registro reutilizando los mismos componentes ya calculados, sin volver a
/// llamar a Python; lock por VectorId para que requests concurrentes sobre
/// la misma capa no dupliquen la llamada.
/// </summary>
public sealed class ComponentAnalysisService : IComponentAnalysisService
{
    private readonly IVectorizationService _vectorizationService;
    private readonly IComponentVersionRegistry _componentRegistry;
    private readonly IPythonComponentClient _pythonClient;
    private readonly IFileStorage _fileStorage;
    private readonly ILogger<ComponentAnalysisService> _logger;

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _generationLocks = new();

    public ComponentAnalysisService(
        IVectorizationService vectorizationService,
        IComponentVersionRegistry componentRegistry,
        IPythonComponentClient pythonClient,
        IFileStorage fileStorage,
        ILogger<ComponentAnalysisService> logger)
    {
        _vectorizationService = vectorizationService;
        _componentRegistry = componentRegistry;
        _pythonClient = pythonClient;
        _fileStorage = fileStorage;
        _logger = logger;
    }

    public async Task<ComponentSetResult> AnalyzeAsync(Guid projectId, Guid imageId, Guid vectorId, CancellationToken cancellationToken)
    {
        var vector = _vectorizationService.FindVector(projectId, imageId, vectorId);
        if (vector is null)
        {
            return new ComponentSetResult.NotFound(
                "not_found", "No existe una capa vectorial (VectorVersion) con ese ID para esta imagen.");
        }

        // Sección crítica: buscar en caché -> generar si falta -> guardar,
        // serializada por VectorId para que requests concurrentes sobre la
        // misma capa no dupliquen la llamada a Python -- mismo criterio que
        // VectorLayerService/DimensionService.
        var lockKey = $"{projectId:N}/{imageId:N}/{vectorId:N}";
        var gate = _generationLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            // VectorVersion es inmutable una vez generada: el mismo VectorId
            // siempre referencia el mismo SVG, así que alcanza como única
            // clave de caché -- a diferencia de VectorLayerService, no hay
            // ninguna otra combinación de parámetros que distinga dos
            // análisis distintos del mismo VectorId.
            var cached = _componentRegistry.FindByVectorId(projectId, imageId, vectorId);
            if (cached is not null)
            {
                var cachedVersion = _componentRegistry.NextVersion(projectId, imageId);
                var cachedRecord = cached with { Version = cachedVersion, CreatedAt = DateTimeOffset.UtcNow };
                _componentRegistry.Save(cachedRecord);

                _logger.LogInformation(
                    "Componentes {ComponentSetId} (v{Version}) reutilizados desde caché para el vector {VectorId} de {ProjectId}/{ImageId}",
                    cachedRecord.ComponentSetId, cachedVersion, vectorId, projectId, imageId);

                return new ComponentSetResult.Ready(cachedRecord, FromCache: true);
            }

            Stream sourceContent;
            try
            {
                sourceContent = await _fileStorage.OpenReadAsync(vector.SvgStorageKey, cancellationToken);
            }
            catch (FileNotFoundException ex)
            {
                _logger.LogError(
                    ex,
                    "El SVG de la capa (vector {VectorId}) de {ProjectId}/{ImageId} ya no está disponible en el storage",
                    vectorId, projectId, imageId);
                return new ComponentSetResult.UpstreamError(
                    "storage_failure", "El SVG de la capa ya no está disponible en el storage.");
            }

            PythonComponentResult pythonResult;
            await using (sourceContent)
            {
                pythonResult = await _pythonClient.AnalyzeAsync(sourceContent, vector.ContentType, "layer.svg", cancellationToken);
            }

            if (pythonResult.State != PythonComponentState.Success)
            {
                return new ComponentSetResult.UpstreamError(
                    MapErrorCode(pythonResult.State),
                    pythonResult.Message ?? "No se pudo analizar los componentes de la capa.");
            }

            var version = _componentRegistry.NextVersion(projectId, imageId);
            var record = new ComponentSetVersion(
                projectId,
                imageId,
                version,
                Guid.NewGuid(),
                vectorId,
                pythonResult.Components!,
                pythonResult.SkippedPathCount!.Value,
                DateTimeOffset.UtcNow);
            _componentRegistry.Save(record);

            _logger.LogInformation(
                "Componentes {ComponentSetId} (v{Version}) calculados para el vector {VectorId} de {ProjectId}/{ImageId} ({ComponentCount} piezas)",
                record.ComponentSetId, version, vectorId, projectId, imageId, record.Components.Count);

            return new ComponentSetResult.Ready(record, FromCache: false);
        }
        finally
        {
            gate.Release();
        }
    }

    public ComponentSetVersion? FindLatest(Guid projectId, Guid imageId, Guid vectorId) =>
        _componentRegistry.FindByVectorId(projectId, imageId, vectorId);

    private static string MapErrorCode(PythonComponentState state) => state switch
    {
        PythonComponentState.InvalidInputSvg => "invalid_input_svg",
        PythonComponentState.SvgTooLarge => "svg_too_large",
        PythonComponentState.TooManySubpaths => "too_many_subpaths",
        PythonComponentState.InvalidParameters => "invalid_parameters",
        PythonComponentState.Timeout => "timeout",
        PythonComponentState.EngineError => "processing_error",
        PythonComponentState.Unavailable => "engine_unavailable",
        PythonComponentState.InvalidResponse => "invalid_response",
        // Igual que InvalidResponse: la respuesta de Python no pasa la validación defensiva
        // adicional del cliente -- Vectify.Api no confía en su contenido.
        PythonComponentState.InvalidSvg => "invalid_response",
        _ => "processing_error",
    };
}
