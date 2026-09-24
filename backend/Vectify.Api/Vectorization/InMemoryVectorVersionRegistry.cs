using System.Collections.Concurrent;

namespace Vectify.Api.Vectorization;

/// <summary>
/// Implementación en memoria de <see cref="IVectorVersionRegistry"/>,
/// registrada como singleton. Mismo criterio que
/// InMemoryThresholdConfigRegistry: suficiente para este sprint (sin base de
/// datos de negocio todavía).
/// </summary>
public sealed class InMemoryVectorVersionRegistry : IVectorVersionRegistry
{
    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId), int> _versions = new();
    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId, Guid SourceMaskId, string ParamsKey), VectorVersion> _byParams = new();
    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId), VectorVersion> _latest = new();
    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId, Guid VectorId), VectorVersion> _byVectorId = new();

    public VectorVersion? FindByParams(Guid projectId, Guid imageId, Guid sourceMaskId, VectorParameters parameters) =>
        _byParams.GetValueOrDefault((projectId, imageId, sourceMaskId, parameters.ToCacheKey()));

    public VectorVersion? FindLatest(Guid projectId, Guid imageId) =>
        _latest.GetValueOrDefault((projectId, imageId));

    public VectorVersion? FindByVectorId(Guid projectId, Guid imageId, Guid vectorId) =>
        _byVectorId.GetValueOrDefault((projectId, imageId, vectorId));

    public int NextVersion(Guid projectId, Guid imageId) =>
        _versions.AddOrUpdate((projectId, imageId), addValueFactory: _ => 1, updateValueFactory: (_, current) => current + 1);

    public void Save(VectorVersion record)
    {
        var imageKey = (record.ProjectId, record.ImageId);
        _byParams[(record.ProjectId, record.ImageId, record.SourceMaskId, record.Parameters.ToCacheKey())] = record;
        _latest[imageKey] = record;
        _byVectorId[(record.ProjectId, record.ImageId, record.VectorId)] = record;
    }
}
