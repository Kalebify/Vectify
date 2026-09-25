using System.Collections.Concurrent;

namespace Vectify.Api.Dimensioning;

/// <summary>Implementación en memoria de <see cref="IDimensionVersionRegistry"/>, usada en tests unitarios. Mismo criterio que InMemorySimplificationVersionRegistry.</summary>
public sealed class InMemoryDimensionVersionRegistry : IDimensionVersionRegistry
{
    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId), int> _versions = new();
    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId, Guid SourceId, DimensionSourceKind SourceKind, string ParamsKey), DimensionVersion> _byParams = new();
    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId, Guid DimensionId), DimensionVersion> _byDimensionId = new();

    public DimensionVersion? FindByParams(
        Guid projectId, Guid imageId, Guid sourceId, DimensionSourceKind sourceKind, DimensionParameters parameters) =>
        _byParams.GetValueOrDefault((projectId, imageId, sourceId, sourceKind, parameters.ToCacheKey()));

    public DimensionVersion? FindByDimensionId(Guid projectId, Guid imageId, Guid dimensionId) =>
        _byDimensionId.GetValueOrDefault((projectId, imageId, dimensionId));

    public int NextVersion(Guid projectId, Guid imageId) =>
        _versions.AddOrUpdate((projectId, imageId), addValueFactory: _ => 1, updateValueFactory: (_, current) => current + 1);

    public void Save(DimensionVersion record)
    {
        _byParams[(record.ProjectId, record.ImageId, record.SourceId, record.SourceKind, record.Parameters.ToCacheKey())] = record;
        _byDimensionId[(record.ProjectId, record.ImageId, record.DimensionId)] = record;
    }
}
