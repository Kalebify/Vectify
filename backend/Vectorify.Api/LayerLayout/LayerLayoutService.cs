using System.Collections.Concurrent;
using Vectorify.Api.VectorLayers;

namespace Vectorify.Api.LayerLayout;

/// <summary>
/// Implementación de <see cref="ILayerLayoutService"/>: resuelve el conjunto
/// de capas vigente de la paleta (vía <see cref="IVectorLayerService.FindLatest"/>
/// -- NUNCA llama a Python ni dispara un cálculo nuevo, solo lectura), valida
/// que el groupId indicado exista entre esas capas (para Visible/Locked/Name)
/// o que el nuevo orden sea exactamente una permutación de esas capas (para
/// Reorder), y guarda una nueva versión del layout que preserva intactos los
/// valores de las demás capas. Lock por paleta+versión confirmada (mismo
/// criterio que ManufacturingOperationService) para que mutaciones
/// concurrentes sobre el mismo conjunto no pisen el avance de versión una de
/// la otra.
/// </summary>
public sealed class LayerLayoutService : ILayerLayoutService
{
    private readonly IVectorLayerService _vectorLayerService;
    private readonly ILayerLayoutVersionRegistry _registry;
    private readonly ILogger<LayerLayoutService> _logger;

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _sessionLocks = new();

    public LayerLayoutService(
        IVectorLayerService vectorLayerService,
        ILayerLayoutVersionRegistry registry,
        ILogger<LayerLayoutService> logger)
    {
        _vectorLayerService = vectorLayerService;
        _registry = registry;
        _logger = logger;
    }

    public Task<LayerLayoutResult> SetVisibleAsync(
        Guid projectId, Guid imageId, Guid paletteId, Guid groupId, bool visible, CancellationToken cancellationToken) =>
        MutateEntryAsync(projectId, imageId, paletteId, groupId, entry => entry with { Visible = visible }, cancellationToken);

    public Task<LayerLayoutResult> SetLockedAsync(
        Guid projectId, Guid imageId, Guid paletteId, Guid groupId, bool locked, CancellationToken cancellationToken) =>
        MutateEntryAsync(projectId, imageId, paletteId, groupId, entry => entry with { Locked = locked }, cancellationToken);

    public Task<LayerLayoutResult> SetNameAsync(
        Guid projectId, Guid imageId, Guid paletteId, Guid groupId, string name, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Task.FromResult<LayerLayoutResult>(
                new LayerLayoutResult.ValidationFailed("invalid_parameters", "El nombre no puede estar vacío."));
        }

        var trimmedName = name.Trim();
        return MutateEntryAsync(projectId, imageId, paletteId, groupId, entry => entry with { Name = trimmedName }, cancellationToken);
    }

    private async Task<LayerLayoutResult> MutateEntryAsync(
        Guid projectId,
        Guid imageId,
        Guid paletteId,
        Guid groupId,
        Func<LayerLayoutEntry, LayerLayoutEntry> mutate,
        CancellationToken cancellationToken)
    {
        var layerSet = _vectorLayerService.FindLatest(projectId, imageId, paletteId);
        if (layerSet is null)
        {
            return new LayerLayoutResult.NotFound(
                "not_found", "No existe un conjunto de capas (M2-S02) generado para esa paleta.");
        }

        if (layerSet.Layers.All(l => l.GroupId != groupId))
        {
            return new LayerLayoutResult.NotFound(
                "group_not_found", "No existe esa capa en el conjunto de capas vigente de esa paleta.");
        }

        var gate = SessionGate(projectId, imageId, paletteId, layerSet.PaletteVersion);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var baseline = LayerLayoutDefaults.Resolve(layerSet, _registry.FindLatest(projectId, imageId, paletteId, layerSet.PaletteVersion));
            var newEntries = baseline
                .Select(entry => entry.GroupId == groupId ? mutate(entry) : entry)
                .ToList();

            return Save(projectId, imageId, paletteId, layerSet, newEntries);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<LayerLayoutResult> ReorderAsync(
        Guid projectId, Guid imageId, Guid paletteId, IReadOnlyList<Guid> orderedGroupIds, CancellationToken cancellationToken)
    {
        var layerSet = _vectorLayerService.FindLatest(projectId, imageId, paletteId);
        if (layerSet is null)
        {
            return new LayerLayoutResult.NotFound(
                "not_found", "No existe un conjunto de capas (M2-S02) generado para esa paleta.");
        }

        var currentGroupIds = layerSet.Layers.Select(l => l.GroupId).ToHashSet();
        var distinctRequested = orderedGroupIds.ToHashSet();
        var isExactPermutation =
            orderedGroupIds.Count == currentGroupIds.Count &&
            distinctRequested.Count == orderedGroupIds.Count &&
            distinctRequested.SetEquals(currentGroupIds);

        if (!isExactPermutation)
        {
            return new LayerLayoutResult.ValidationFailed(
                "invalid_parameters",
                "El nuevo orden debe incluir, exactamente una vez cada uno, todos los groupId del conjunto de capas vigente -- ninguno de más, ninguno de menos.");
        }

        var gate = SessionGate(projectId, imageId, paletteId, layerSet.PaletteVersion);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var baseline = LayerLayoutDefaults.Resolve(layerSet, _registry.FindLatest(projectId, imageId, paletteId, layerSet.PaletteVersion));
            var byGroupId = baseline.ToDictionary(e => e.GroupId);

            // Reescribe SOLO Order (índice en la nueva secuencia); Visible/Locked de
            // cada capa se preservan intactos -- reordenar NUNCA toca d/transform/
            // geometría, ni siquiera indirectamente vía otros campos de layout.
            var newEntries = orderedGroupIds
                .Select((groupId, index) => byGroupId[groupId] with { Order = index })
                .ToList();

            return Save(projectId, imageId, paletteId, layerSet, newEntries);
        }
        finally
        {
            gate.Release();
        }
    }

    public (VectorLayerSetVersion LayerSet, LayerLayoutSetVersion? Layout)? FindCurrent(
        Guid projectId, Guid imageId, Guid paletteId)
    {
        var layerSet = _vectorLayerService.FindLatest(projectId, imageId, paletteId);
        if (layerSet is null)
        {
            return null;
        }

        var layout = _registry.FindLatest(projectId, imageId, paletteId, layerSet.PaletteVersion);
        return (layerSet, layout);
    }

    private LayerLayoutResult Save(
        Guid projectId, Guid imageId, Guid paletteId, VectorLayerSetVersion layerSet, IReadOnlyList<LayerLayoutEntry> entries)
    {
        var version = _registry.NextVersion(projectId, imageId, paletteId, layerSet.PaletteVersion);
        var record = new LayerLayoutSetVersion(
            projectId, imageId, paletteId, layerSet.PaletteVersion, layerSet.LayerSetId, version, entries, DateTimeOffset.UtcNow);
        _registry.Save(record);

        _logger.LogInformation(
            "Layout (order/visible/locked) de la paleta {PaletteId} (v{PaletteVersion}) de {ProjectId}/{ImageId} actualizado (layout v{Version})",
            paletteId, layerSet.PaletteVersion, projectId, imageId, version);

        return new LayerLayoutResult.Ready(record, layerSet);
    }

    private static SemaphoreSlim SessionGate(Guid projectId, Guid imageId, Guid paletteId, int paletteVersion) =>
        _sessionLocks.GetOrAdd($"{projectId:N}/{imageId:N}/{paletteId:N}/{paletteVersion}", _ => new SemaphoreSlim(1, 1));
}
