namespace Vectify.Api.Contracts;

/// <summary>Una capa vectorial tal como la ve React -- ver Vectify.Api.VectorLayers.VectorLayer. SvgUrl reutiliza el endpoint GET .../vectors/{vectorId} ya existente de M1-S05.</summary>
public sealed record VectorLayerPayload(
    Guid GroupId,
    string Name,
    string ColorHex,
    double AreaPercent,
    bool HasPartialAlpha,
    Guid VectorId,
    string SvgUrl);

/// <summary>
/// Respuesta de POST .../color-palette/{paletteId}/layers y GET
/// .../color-palette/{paletteId}/layers: siempre la ÚLTIMA versión vigente
/// del conjunto de capas de una sesión de paleta, con una entrada por cada
/// color/grupo confirmado.
/// </summary>
public sealed record VectorLayerSetResponse(
    Guid ProjectId,
    Guid ImageId,
    Guid LayerSetId,
    int Version,
    Guid PaletteId,
    int PaletteVersion,
    int SourceWidthPx,
    int SourceHeightPx,
    IReadOnlyList<VectorLayerPayload> Layers,
    bool Cached);
