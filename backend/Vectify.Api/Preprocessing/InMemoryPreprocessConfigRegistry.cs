using System.Collections.Concurrent;

namespace Vectify.Api.Preprocessing;

/// <summary>
/// Implementación en memoria de <see cref="IPreprocessConfigRegistry"/>, registrada
/// como singleton. Suficiente para este sprint (sin base de datos de negocio
/// todavía, mismo criterio que InMemoryProjectRegistry de M1-S02).
/// </summary>
public sealed class InMemoryPreprocessConfigRegistry : IPreprocessConfigRegistry
{
    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId), int> _versions = new();
    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId, string ParamsKey), PreprocessConfigRecord> _byParams = new();
    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId), PreprocessConfigRecord> _latest = new();
    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId, Guid PreviewId), PreprocessConfigRecord> _byPreviewId = new();

    public PreprocessConfigRecord? FindByParams(Guid projectId, Guid imageId, PreprocessParameters parameters) =>
        _byParams.GetValueOrDefault((projectId, imageId, parameters.ToCacheKey()));

    public PreprocessConfigRecord? FindLatest(Guid projectId, Guid imageId) =>
        _latest.GetValueOrDefault((projectId, imageId));

    public PreprocessConfigRecord? FindByPreviewId(Guid projectId, Guid imageId, Guid previewId) =>
        _byPreviewId.GetValueOrDefault((projectId, imageId, previewId));

    public int NextVersion(Guid projectId, Guid imageId) =>
        _versions.AddOrUpdate((projectId, imageId), addValueFactory: _ => 1, updateValueFactory: (_, current) => current + 1);

    public void Save(PreprocessConfigRecord record)
    {
        var imageKey = (record.ProjectId, record.ImageId);
        _byParams[(record.ProjectId, record.ImageId, record.Parameters.ToCacheKey())] = record;
        _latest[imageKey] = record;
        _byPreviewId[(record.ProjectId, record.ImageId, record.PreviewId)] = record;
    }
}
