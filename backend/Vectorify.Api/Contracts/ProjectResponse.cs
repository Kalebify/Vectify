namespace Vectorify.Api.Contracts;

/// <summary>
/// DTO versionado (M2.2-S03) de un <see cref="Vectorify.Api.Data.Project"/> completo --
/// NUNCA se expone la entidad EF directamente en una respuesta JSON (mismo patrón que
/// <see cref="LayerLayoutSetResponse"/>/<see cref="ApiErrorResponse"/>). Respuesta de
/// POST/GET/PATCH/duplicate de un único proyecto bajo <c>/api/v2/projects</c>.
/// </summary>
public sealed record ProjectResponse(
    Guid Id,
    Guid OwnerId,
    string Name,
    string? Description,
    Guid? ThumbnailAssetId,
    Guid? CurrentVersionId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// DTO más liviano (M2.2-S03) para <c>GET /api/v2/projects</c> (listado): omite
/// OwnerId/CurrentVersionId, que el listado de un usuario no necesita (siempre es el suyo)
/// y que recargarían innecesariamente una respuesta con potencialmente 50+ proyectos (ver
/// spec.md, escala de referencia de M2.2-S08).
/// </summary>
public sealed record ProjectSummaryResponse(
    Guid Id,
    string Name,
    Guid? ThumbnailAssetId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Página de <see cref="ProjectSummaryResponse"/> -- TotalCount es el total ANTES de paginar, para que el cliente calcule cuántas páginas hay.</summary>
public sealed record ProjectListResponse(
    IReadOnlyList<ProjectSummaryResponse> Items,
    int Page,
    int PageSize,
    int TotalCount);
