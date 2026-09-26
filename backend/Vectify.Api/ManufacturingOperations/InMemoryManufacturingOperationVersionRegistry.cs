using System.Collections.Concurrent;

namespace Vectify.Api.ManufacturingOperations;

/// <summary>Implementación en memoria de <see cref="IManufacturingOperationVersionRegistry"/>, usada en tests unitarios. Mismo criterio que InMemoryComponentGroupVersionRegistry.</summary>
public sealed class InMemoryManufacturingOperationVersionRegistry : IManufacturingOperationVersionRegistry
{
    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId, Guid PaletteId, int PaletteVersion), int> _versions = new();
    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId, Guid PaletteId, int PaletteVersion), ManufacturingOperationSetVersion> _latest = new();

    public ManufacturingOperationSetVersion? FindLatest(Guid projectId, Guid imageId, Guid paletteId, int paletteVersion) =>
        _latest.GetValueOrDefault((projectId, imageId, paletteId, paletteVersion));

    public int NextVersion(Guid projectId, Guid imageId, Guid paletteId, int paletteVersion) =>
        _versions.AddOrUpdate((projectId, imageId, paletteId, paletteVersion), addValueFactory: _ => 1, updateValueFactory: (_, current) => current + 1);

    public void Save(ManufacturingOperationSetVersion record) =>
        _latest[(record.ProjectId, record.ImageId, record.PaletteId, record.PaletteVersion)] = record;
}
