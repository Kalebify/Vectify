using System.Collections.Concurrent;

namespace Vectify.Api.Projects;

/// <summary>
/// Implementación en memoria de <see cref="IProjectRegistry"/>, registrada como
/// singleton. Suficiente para este sprint (sin base de datos de negocio todavía).
/// </summary>
public sealed class InMemoryProjectRegistry : IProjectRegistry
{
    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId), ProjectRecord> _byId = new();
    private readonly ConcurrentDictionary<string, ProjectRecord> _byIdempotencyKey = new(StringComparer.Ordinal);

    public void Save(ProjectRecord record)
    {
        _byId[(record.ProjectId, record.ImageId)] = record;

        if (!string.IsNullOrWhiteSpace(record.IdempotencyKey))
        {
            _byIdempotencyKey[record.IdempotencyKey] = record;
        }
    }

    public ProjectRecord? Find(Guid projectId, Guid imageId) =>
        _byId.GetValueOrDefault((projectId, imageId));

    public ProjectRecord? FindByIdempotencyKey(string idempotencyKey) =>
        string.IsNullOrWhiteSpace(idempotencyKey) ? null : _byIdempotencyKey.GetValueOrDefault(idempotencyKey);
}
