using System.Collections.Concurrent;
using Vectify.Api.VectorLayers;

namespace Vectify.Api.ManufacturingOperations;

/// <summary>
/// Implementación de <see cref="IManufacturingOperationService"/>: resuelve
/// el conjunto de capas vigente de la paleta (vía
/// <see cref="IVectorLayerService.FindLatest"/> -- NUNCA llama a Python ni
/// dispara un cálculo nuevo, solo lectura), valida que el groupId indicado
/// exista entre esas capas y que el valor de operación sea uno de los 3
/// aceptados, y guarda una nueva versión del conjunto de asignaciones que
/// preserva intactas las asignaciones de las demás capas. Lock por
/// paleta+versión confirmada para que asignaciones concurrentes sobre el
/// mismo conjunto no pisen el avance de versión una de la otra -- mismo
/// criterio que ComponentGroupService (lock por VectorId).
/// </summary>
public sealed class ManufacturingOperationService : IManufacturingOperationService
{
    private readonly IVectorLayerService _vectorLayerService;
    private readonly IManufacturingOperationVersionRegistry _registry;
    private readonly ILogger<ManufacturingOperationService> _logger;

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _sessionLocks = new();

    public ManufacturingOperationService(
        IVectorLayerService vectorLayerService,
        IManufacturingOperationVersionRegistry registry,
        ILogger<ManufacturingOperationService> logger)
    {
        _vectorLayerService = vectorLayerService;
        _registry = registry;
        _logger = logger;
    }

    public async Task<ManufacturingOperationResult> AssignAsync(
        Guid projectId, Guid imageId, Guid paletteId, Guid groupId, string? operation, CancellationToken cancellationToken)
    {
        var layerSet = _vectorLayerService.FindLatest(projectId, imageId, paletteId);
        if (layerSet is null)
        {
            return new ManufacturingOperationResult.NotFound(
                "not_found", "No existe un conjunto de capas (M2-S02) generado para esa paleta.");
        }

        if (layerSet.Layers.All(l => l.GroupId != groupId))
        {
            return new ManufacturingOperationResult.NotFound(
                "group_not_found", "No existe esa capa en el conjunto de capas vigente de esa paleta.");
        }

        var parsed = ManufacturingOperationParser.Parse(operation);
        if (parsed is null)
        {
            return new ManufacturingOperationResult.ValidationFailed(
                "invalid_parameters",
                $"operation '{operation}' desconocido. Valores válidos: cut, engrave, ignore.");
        }

        var gate = SessionGate(projectId, imageId, paletteId, layerSet.PaletteVersion);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var latest = _registry.FindLatest(projectId, imageId, paletteId, layerSet.PaletteVersion);
            var existingAssignments = latest?.Assignments ?? Array.Empty<ManufacturingOperationAssignment>();

            var newAssignments = existingAssignments
                .Where(a => a.GroupId != groupId)
                .Append(new ManufacturingOperationAssignment(groupId, parsed.Value, DateTimeOffset.UtcNow))
                .ToList();

            var version = _registry.NextVersion(projectId, imageId, paletteId, layerSet.PaletteVersion);
            var record = new ManufacturingOperationSetVersion(
                projectId, imageId, paletteId, layerSet.PaletteVersion, layerSet.LayerSetId, version, newAssignments, DateTimeOffset.UtcNow);
            _registry.Save(record);

            _logger.LogInformation(
                "Capa {GroupId} de la paleta {PaletteId} (v{PaletteVersion}) de {ProjectId}/{ImageId} asignada a {Operation} (asignaciones v{Version})",
                groupId, paletteId, layerSet.PaletteVersion, projectId, imageId, parsed.Value, version);

            return new ManufacturingOperationResult.Ready(record, layerSet);
        }
        finally
        {
            gate.Release();
        }
    }

    public (VectorLayerSetVersion LayerSet, ManufacturingOperationSetVersion? Assignments)? FindCurrent(
        Guid projectId, Guid imageId, Guid paletteId)
    {
        var layerSet = _vectorLayerService.FindLatest(projectId, imageId, paletteId);
        if (layerSet is null)
        {
            return null;
        }

        var assignments = _registry.FindLatest(projectId, imageId, paletteId, layerSet.PaletteVersion);
        return (layerSet, assignments);
    }

    private static SemaphoreSlim SessionGate(Guid projectId, Guid imageId, Guid paletteId, int paletteVersion) =>
        _sessionLocks.GetOrAdd($"{projectId:N}/{imageId:N}/{paletteId:N}/{paletteVersion}", _ => new SemaphoreSlim(1, 1));
}
