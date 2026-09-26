using System.Collections.Concurrent;

namespace Vectify.Api.VectorLayers;

/// <summary>
/// Implementación en memoria de <see cref="IVectorLayerSetRegistry"/>,
/// registrada como singleton. Mismo criterio que InMemoryColorPaletteVersionRegistry.
/// </summary>
public sealed class InMemoryVectorLayerSetRegistry : IVectorLayerSetRegistry
{
    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId, Guid PaletteId), int> _versions = new();
    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId, Guid PaletteId, int PaletteVersion), VectorLayerSetVersion> _byParams = new();
    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId, Guid PaletteId), VectorLayerSetVersion> _latest = new();

    public VectorLayerSetVersion? FindByParams(Guid projectId, Guid imageId, Guid paletteId, int paletteVersion) =>
        _byParams.GetValueOrDefault((projectId, imageId, paletteId, paletteVersion));

    public VectorLayerSetVersion? FindLatest(Guid projectId, Guid imageId, Guid paletteId) =>
        _latest.GetValueOrDefault((projectId, imageId, paletteId));

    public int NextVersion(Guid projectId, Guid imageId, Guid paletteId) =>
        _versions.AddOrUpdate((projectId, imageId, paletteId), addValueFactory: _ => 1, updateValueFactory: (_, current) => current + 1);

    public void Save(VectorLayerSetVersion record)
    {
        var sessionKey = (record.ProjectId, record.ImageId, record.PaletteId);
        _byParams[(record.ProjectId, record.ImageId, record.PaletteId, record.PaletteVersion)] = record;
        _latest[sessionKey] = record;
    }
}
