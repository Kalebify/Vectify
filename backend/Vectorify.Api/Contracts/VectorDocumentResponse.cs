namespace Vectorify.Api.Contracts;

/// <summary>Respuesta de <c>POST /api/v2/workspaces/save</c> (M2.2-S05): confirmación MÍNIMA de que el Save se persistió -- el cliente ya tiene el resto del documento en memoria (es el que acaba de mandar a resolver).</summary>
public sealed record VectorDocumentSaveResponse(Guid ProjectId, int VersionNumber, DateTimeOffset SavedAt);

/// <summary>
/// DTO versionado (M2.2-S05) de la <c>DocumentVersion</c> ACTUAL de un proyecto completa --
/// NUNCA se expone la entidad EF directamente (mismo patrón que <see cref="ProjectResponse"/>).
/// Respuesta de <c>GET /api/v2/projects/{projectId}/document</c>: lo que la reapertura del
/// Workspace usa para reconstruir el <c>VectorDocument</c> sin pasar por el flujo clásico.
/// </summary>
public sealed record VectorDocumentResponse(
    Guid ProjectId,
    int SchemaVersion,
    double WidthMm,
    double HeightMm,
    string ViewBox,
    int VersionNumber,
    DateTimeOffset CreatedAt,
    IReadOnlyList<VectorDocumentLayerResponse> Layers);

/// <summary>
/// Una capa dentro de <see cref="VectorDocumentResponse"/> (o respuesta individual de
/// <c>PATCH .../layers/{layerId}</c>). <see cref="SvgUrl"/> es null si <see cref="SvgAssetId"/>
/// es null (defensivo -- en la práctica el flujo de Save siempre sube el SVG antes de escribir
/// la fila); cuando no es null, apunta al endpoint de descarga YA EXISTENTE de Assets
/// (<c>GET /api/v2/projects/{projectId}/assets/{assetId}</c>, M2.2-S04) -- no se reinventa un
/// endpoint de descarga paralelo.
/// </summary>
public sealed record VectorDocumentLayerResponse(
    Guid Id,
    string Name,
    int Order,
    bool Visible,
    bool Locked,
    string ManufacturingOperation,
    string ColorHex,
    double Coverage,
    bool IsBackground,
    Guid? SvgAssetId,
    string? SvgUrl,
    int PathCount);
