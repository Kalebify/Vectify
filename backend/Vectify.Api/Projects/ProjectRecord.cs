namespace Vectify.Api.Projects;

/// <summary>
/// Metadatos de un proyecto creado a partir de una imagen. Vive en memoria para
/// lecturas O(1) y se persiste como sidecar JSON en disco (ver
/// <see cref="PersistentProjectRegistry"/>), así que sobrevive a un reinicio de la
/// Web API aunque todavía no haya una base de datos de negocio (ver Fuera de alcance
/// de M1-S01/M1-S02).
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
