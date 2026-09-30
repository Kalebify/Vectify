using System.Collections.Concurrent;

namespace Vectorify.Api.LayerLayout;

/// <summary>Implementación en memoria de <see cref="ILayerLayoutVersionRegistry"/>, usada en tests unitarios. Mismo criterio que InMemoryManufacturingOperationVersionRegistry.</summary>
public sealed class InMemoryLayerLayoutVersionRegistry : ILayerLayoutVersionRegistry
{
    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId, Guid PaletteId, int PaletteVersion), int> _versions = new();
    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId, Guid PaletteId, int PaletteVersion), LayerLayoutSetVersion> _latest = new();

    public LayerLayoutSetVersion? FindLatest(Guid projectId, Guid imageId, Guid paletteId, int paletteVersion) =>
        _latest.GetValueOrDefault((projectId, imageId, paletteId, paletteVersion));

    public int NextVersion(Guid projectId, Guid imageId, Guid paletteId, int paletteVersion) =>
        _versions.AddOrUpdate((projectId, imageId, paletteId, paletteVersion), addValueFactory: _ => 1, updateValueFactory: (_, current) => current + 1);

    public void Save(LayerLayoutSetVersion record) =>
        _latest[(record.ProjectId, record.ImageId, record.PaletteId, record.PaletteVersion)] = record;
}
