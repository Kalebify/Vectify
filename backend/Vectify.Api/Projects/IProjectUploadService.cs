namespace Vectify.Api.Projects;

/// <summary>Orquesta validar, guardar y registrar un proyecto a partir de una imagen subida.</summary>
public interface IProjectUploadService
{
    Task<ProjectUploadResult> UploadAsync(IFormFile? file, string? idempotencyKey, CancellationToken cancellationToken);
}
