namespace Vectorify.Api.Contracts;

/// <summary>
/// DTO versionado (M2.2-S04) de un <see cref="Vectorify.Api.Data.Asset"/> -- nunca se expone
/// la entidad EF directamente en una respuesta JSON (mismo patrón que
/// <see cref="ProjectResponse"/>). Respuesta de POST/GET de metadata bajo
/// <c>/api/v2/projects/{projectId}/assets</c>. El binario en sí se sirve aparte, en
/// streaming, vía <c>GET /api/v2/projects/{projectId}/assets/{assetId}</c> (sin pasar por
/// este DTO).
/// </summary>
public sealed record AssetResponse(
    Guid Id,
    Guid ProjectId,
    string Type,
    string MimeType,
    string FileName,
    long Size,
    int? Width,
    int? Height,
    string Checksum,
    DateTimeOffset CreatedAt);
