using System.Collections.Concurrent;

namespace Vectify.Api.PhysicalUnion;

/// <summary>Implementación en memoria de <see cref="IPhysicalUnionVersionRegistry"/>, usada en tests unitarios. Mismo criterio que InMemoryComponentGroupVersionRegistry.</summary>
public sealed class InMemoryPhysicalUnionVersionRegistry : IPhysicalUnionVersionRegistry
{
    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId, Guid SourceVectorId), int> _versions = new();
    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId, Guid SourceVectorId), PhysicalUnionVersion> _latest = new();

    public PhysicalUnionVersion? FindLatest(Guid projectId, Guid imageId, Guid sourceVectorId) =>
        _latest.GetValueOrDefault((projectId, imageId, sourceVectorId));

    public int NextVersion(Guid projectId, Guid imageId, Guid sourceVectorId) =>
        _versions.AddOrUpdate((projectId, imageId, sourceVectorId), addValueFactory: _ => 1, updateValueFactory: (_, current) => current + 1);

    public void Save(PhysicalUnionVersion record) =>
        _latest[(record.ProjectId, record.ImageId, record.SourceVectorId)] = record;
}
