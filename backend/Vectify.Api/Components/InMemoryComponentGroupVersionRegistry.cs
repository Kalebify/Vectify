using System.Collections.Concurrent;

namespace Vectify.Api.Components;

/// <summary>Implementación en memoria de <see cref="IComponentGroupVersionRegistry"/>, usada en tests unitarios. Mismo criterio que InMemoryComponentVersionRegistry.</summary>
public sealed class InMemoryComponentGroupVersionRegistry : IComponentGroupVersionRegistry
{
    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId, Guid VectorId), int> _versions = new();
    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId, Guid VectorId), ComponentGroupSetVersion> _latest = new();

    public ComponentGroupSetVersion? FindLatest(Guid projectId, Guid imageId, Guid vectorId) =>
        _latest.GetValueOrDefault((projectId, imageId, vectorId));

    public int NextVersion(Guid projectId, Guid imageId, Guid vectorId) =>
        _versions.AddOrUpdate((projectId, imageId, vectorId), addValueFactory: _ => 1, updateValueFactory: (_, current) => current + 1);

    public void Save(ComponentGroupSetVersion record) =>
        _latest[(record.ProjectId, record.ImageId, record.VectorId)] = record;
}
