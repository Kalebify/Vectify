using System.Collections.Concurrent;

namespace Vectify.Api.Threshold;

/// <summary>
/// Implementación en memoria de <see cref="IThresholdConfigRegistry"/>,
/// registrada como singleton. Mismo criterio que
/// InMemoryPreprocessConfigRegistry: suficiente para este sprint (sin base de
/// datos de negocio todavía).
/// </summary>
public sealed class InMemoryThresholdConfigRegistry : IThresholdConfigRegistry
{
    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId), int> _versions = new();
    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId, Guid SourcePreviewId, string ParamsKey), ThresholdConfigRecord> _byParams = new();
    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId), ThresholdConfigRecord> _latest = new();
    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId, Guid MaskId), ThresholdConfigRecord> _byMaskId = new();

    public ThresholdConfigRecord? FindByParams(Guid projectId, Guid imageId, Guid sourcePreviewId, ThresholdParameters parameters) =>
        _byParams.GetValueOrDefault((projectId, imageId, sourcePreviewId, parameters.ToCacheKey()));

    public ThresholdConfigRecord? FindLatest(Guid projectId, Guid imageId) =>
        _latest.GetValueOrDefault((projectId, imageId));

    public ThresholdConfigRecord? FindByMaskId(Guid projectId, Guid imageId, Guid maskId) =>
        _byMaskId.GetValueOrDefault((projectId, imageId, maskId));

    public int NextVersion(Guid projectId, Guid imageId) =>
        _versions.AddOrUpdate((projectId, imageId), addValueFactory: _ => 1, updateValueFactory: (_, current) => current + 1);

    public void Save(ThresholdConfigRecord record)
    {
        var imageKey = (record.ProjectId, record.ImageId);
        _byParams[(record.ProjectId, record.ImageId, record.SourcePreviewId, record.Parameters.ToCacheKey())] = record;
        _latest[imageKey] = record;
        _byMaskId[(record.ProjectId, record.ImageId, record.MaskId)] = record;
    }
}
