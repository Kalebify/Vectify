namespace Vectorify.Api.Contracts;

/// <summary>
/// Color RGB explícito (M2.1-S02, contrato sugerido "colors[{...rgb}]" de
/// spec.md) -- aditivo, además de <see cref="ColorGroupPayload.ColorHex"/>
/// ya existente (nunca lo reemplaza, no rompe a los consumidores actuales).
/// </summary>
public sealed record RgbColor(int R, int G, int B);

/// <summary>
/// Un color/grupo tal como lo ve React -- ver Vectorify.Api.ColorPalette.ColorGroup.
/// <see cref="IsExcluded"/> (M2.1-S02, NUEVO): incluido (false)/excluido
/// (true), independiente de un merge. Pre-marcado por Vectorify.Api en la
/// primera detección para el fondo dominante detectado automáticamente (ver
/// ColorPaletteService.DetectAsync), pero el usuario puede cambiarlo en
/// cualquier momento (POST .../color-palette/{paletteId}/exclude) sin que
/// afecte a ningún otro grupo.
/// </summary>
public sealed record ColorGroupPayload(
    Guid GroupId,
    string Name,
    string ColorHex,
    RgbColor Rgb,
    long PixelCount,
    double AreaPercent,
    bool HasPartialAlpha,
    bool IsExcluded,
    string MaskUrl,
    bool IsMerged);

/// <summary>
/// Respuesta de POST .../color-palette/detect, POST .../merge, POST
/// .../unmerge, POST .../rename, POST .../exclude, POST .../confirm y GET
/// .../color-palette/{paletteId}: siempre la ÚLTIMA versión vigente de la
/// sesión de paleta, con sus grupos actuales y la URL del preview cuantizado
/// ya actualizado.
/// </summary>
public sealed record ColorPaletteResponse(
    Guid ProjectId,
    Guid ImageId,
    Guid PaletteId,
    int Version,
    double Tolerance,
    int? MaxColors,
    double TinyAreaRatio,
    int SourceWidthPx,
    int SourceHeightPx,
    double TransparentPercent,
    IReadOnlyList<ColorGroupPayload> Groups,
    string PreviewUrl,
    bool IsConfirmed,
    bool Cached);
