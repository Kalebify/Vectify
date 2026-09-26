using System.Collections.Concurrent;

namespace Vectify.Api.Components;

/// <summary>Implementación en memoria de <see cref="IComponentVersionRegistry"/>, usada en tests unitarios. Mismo criterio que InMemoryVectorLayerSetRegistry.</summary>
public sealed class InMemoryComponentVersionRegistry : IComponentVersionRegistry
{
    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId), int> _versions = new();
    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId, Guid VectorId), ComponentSetVersion> _byVectorId = new();

    public ComponentSetVersion? FindByVectorId(Guid projectId, Guid imageId, Guid vectorId) =>
        _byVectorId.GetValueOrDefault((projectId, imageId, vectorId));

    public int NextVersion(Guid projectId, Guid imageId) =>
        _versions.AddOrUpdate((projectId, imageId), addValueFactory: _ => 1, updateValueFactory: (_, current) => current + 1);

    public void Save(ComponentSetVersion record) =>
        _byVectorId[(record.ProjectId, record.ImageId, record.VectorId)] = record;
}
