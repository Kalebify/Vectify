namespace Vectify.Api.Projects;

/// <summary>
/// Metadatos de un proyecto creado a partir de una imagen. Vive solo en memoria en
/// este sprint (no hay base de datos de negocio todavía — ver Fuera de alcance de
/// M1-S01/M1-S02); se pierde al reiniciar la Web API.
/// </summary>
public sealed record ProjectRecord(
    Guid ProjectId,
    Guid ImageId,
    string FileName,
    string MimeType,
    long Bytes,
    int? Width,
    int? Height,
    string Status,
    string StorageKey,
    string? IdempotencyKey,
    DateTimeOffset CreatedAt);
