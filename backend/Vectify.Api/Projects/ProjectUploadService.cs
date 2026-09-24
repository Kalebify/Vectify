using Vectify.Api.Contracts;
using Vectify.Api.Imaging;
using Vectify.Api.Storage;
using Vectify.Api.Validation;

namespace Vectify.Api.Projects;

/// <summary>
/// Implementación de <see cref="IProjectUploadService"/>: valida el archivo,
/// genera projectId/imageId, guarda el original mediante <see cref="IFileStorage"/>
/// y registra el proyecto. El original nunca se modifica después de guardado
/// (se escribe una sola vez, bajo una clave nueva por carga).
/// </summary>
public sealed class ProjectUploadService : IProjectUploadService
{
    private readonly IImageUploadValidator _validator;
    private readonly IFileStorage _fileStorage;
    private readonly IProjectRegistry _registry;
    private readonly ILogger<ProjectUploadService> _logger;

    public ProjectUploadService(
        IImageUploadValidator validator,
        IFileStorage fileStorage,
        IProjectRegistry registry,
        ILogger<ProjectUploadService> logger)
    {
        _validator = validator;
        _fileStorage = fileStorage;
        _registry = registry;
        _logger = logger;
    }

    public async Task<ProjectUploadResult> UploadAsync(IFormFile? file, string? idempotencyKey, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var existing = _registry.FindByIdempotencyKey(idempotencyKey);
            if (existing is not null)
            {
                _logger.LogInformation(
                    "Replay de Idempotency-Key {IdempotencyKey}: devolviendo proyecto {ProjectId} sin crear uno nuevo",
                    idempotencyKey,
                    existing.ProjectId);
                return new ProjectUploadResult.Replayed(ToResponse(existing));
            }
        }

        var validation = _validator.Validate(file);
        if (!validation.IsValid)
        {
            return new ProjectUploadResult.ValidationFailed(validation.ErrorCode!, validation.ErrorMessage!);
        }

        var projectId = Guid.NewGuid();
        var imageId = Guid.NewGuid();
        var safeFileName = Path.GetFileName(file!.FileName);
        var storageKey = $"{projectId:N}/{imageId:N}/original{validation.Extension}";

        int? width = null;
        int? height = null;
        await using (var probeStream = file.OpenReadStream())
        {
            if (ImageDimensionsReader.TryRead(probeStream, validation.ContentType!, out var probedWidth, out var probedHeight))
            {
                width = probedWidth;
                height = probedHeight;
            }
        }

        StoredFile stored;
        try
        {
            await using var content = file.OpenReadStream();
            stored = await _fileStorage.SaveAsync(storageKey, content, validation.ContentType!, cancellationToken);
        }
        catch (FileStorageException ex)
        {
            _logger.LogError(ex, "Fallo de storage al guardar el proyecto {ProjectId}/{ImageId}", projectId, imageId);
            return new ProjectUploadResult.StorageFailed(
                "No se pudo guardar el archivo. Intentá de nuevo en unos minutos.");
        }

        var record = new ProjectRecord(
            projectId,
            imageId,
            safeFileName,
            validation.ContentType!,
            stored.SizeBytes,
            width,
            height,
            Status: "uploaded",
            storageKey,
            idempotencyKey,
            DateTimeOffset.UtcNow);

        _registry.Save(record);

        _logger.LogInformation(
            "Proyecto {ProjectId} creado a partir de la imagen {ImageId} ({Bytes} bytes, {MimeType})",
            projectId,
            imageId,
            stored.SizeBytes,
            validation.ContentType);

        return new ProjectUploadResult.Created(ToResponse(record));
    }

    private static UploadImageResponse ToResponse(ProjectRecord record) => new(
        record.ProjectId,
        record.ImageId,
        record.FileName,
        record.MimeType,
        record.Bytes,
        record.Width,
        record.Height,
        record.Status);
}
