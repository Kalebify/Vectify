using System.Collections.Concurrent;

namespace Vectify.Api.Simplification;

/// <summary>
/// Implementación en memoria de <see cref="ISimplificationVersionRegistry"/>,
/// registrada como singleton. Mismo criterio que InMemoryVectorVersionRegistry.
/// </summary>
public sealed class InMemorySimplificationVersionRegistry : ISimplificationVersionRegistry
{
    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId), int> _versions = new();
    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId, Guid SourceVectorId, string ParamsKey), SimplificationVersion> _byParams = new();
    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId), SimplificationVersion> _latest = new();
    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId, Guid SimplificationId), SimplificationVersion> _bySimplificationId = new();

    public SimplificationVersion? FindByParams(Guid projectId, Guid imageId, Guid sourceVectorId, SimplificationParameters parameters) =>
        _byParams.GetValueOrDefault((projectId, imageId, sourceVectorId, parameters.ToCacheKey()));

    public SimplificationVersion? FindLatest(Guid projectId, Guid imageId) =>
        _latest.GetValueOrDefault((projectId, imageId));

    public SimplificationVersion? FindBySimplificationId(Guid projectId, Guid imageId, Guid simplificationId) =>
        _bySimplificationId.GetValueOrDefault((projectId, imageId, simplificationId));

    public int NextVersion(Guid projectId, Guid imageId) =>
        _versions.AddOrUpdate((projectId, imageId), addValueFactory: _ => 1, updateValueFactory: (_, current) => current + 1);

    public void Save(SimplificationVersion record)
    {
        var imageKey = (record.ProjectId, record.ImageId);
        _byParams[(record.ProjectId, record.ImageId, record.SourceVectorId, record.Parameters.ToCacheKey())] = record;
        _latest[imageKey] = record;
        _bySimplificationId[(record.ProjectId, record.ImageId, record.SimplificationId)] = record;
    }
}
