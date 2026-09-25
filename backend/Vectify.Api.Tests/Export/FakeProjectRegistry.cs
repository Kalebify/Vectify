using Vectify.Api.Projects;

namespace Vectify.Api.Tests.Export;

/// <summary>
/// IProjectRegistry en memoria para tests unitarios de ExportService: permite
/// registrar de antemano el ProjectRecord (en particular su FileName
/// original, controlado libremente por el usuario en M1-S02) que Find debe
/// devolver, sin depender de PersistentProjectRegistry ni de disco.
/// </summary>
internal sealed class FakeProjectRegistry : IProjectRegistry
{
    private readonly Dictionary<(Guid ProjectId, Guid ImageId), ProjectRecord> _projects = new();
    private readonly Dictionary<string, ProjectRecord> _byIdempotencyKey = new();

    public void Save(ProjectRecord record)
    {
        _projects[(record.ProjectId, record.ImageId)] = record;
        if (record.IdempotencyKey is not null)
        {
            _byIdempotencyKey[record.IdempotencyKey] = record;
        }
    }

    public ProjectRecord? Find(Guid projectId, Guid imageId) =>
        _projects.GetValueOrDefault((projectId, imageId));

    public ProjectRecord? FindByIdempotencyKey(string idempotencyKey) =>
        _byIdempotencyKey.GetValueOrDefault(idempotencyKey);
}
