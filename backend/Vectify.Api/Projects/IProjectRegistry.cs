namespace Vectify.Api.Projects;

/// <summary>Registro de proyectos creados a partir de una imagen subida.</summary>
public interface IProjectRegistry
{
    void Save(ProjectRecord record);

    ProjectRecord? Find(Guid projectId, Guid imageId);

    ProjectRecord? FindByIdempotencyKey(string idempotencyKey);
}
