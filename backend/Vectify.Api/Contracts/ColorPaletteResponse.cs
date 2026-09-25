namespace Vectify.Api.Contracts;

/// <summary>Un color/grupo tal como lo ve React -- ver Vectify.Api.ColorPalette.ColorGroup.</summary>
public sealed record ColorGroupPayload(
    Guid GroupId,
    string Name,
    string ColorHex,
    long PixelCount,
    double AreaPercent,
    bool HasPartialAlpha,
    string MaskUrl,
    bool IsMerged);

/// <summary>
/// Respuesta de POST .../color-palette/detect, POST .../merge, POST
/// .../unmerge, POST .../rename, POST .../confirm y GET .../color-palette/{paletteId}:
/// siempre la ÚLTIMA versión vigente de la sesión de paleta, con sus grupos
/// actuales y la URL del preview cuantizado ya actualizado.
/// </summary>
public sealed record ColorPaletteResponse(
    Guid ProjectId,
    Guid ImageId,
    Guid PaletteId,
    int Version,
    double Tolerance,
    int? MaxColors,
    int SourceWidthPx,
    int SourceHeightPx,
    double TransparentPercent,
    IReadOnlyList<ColorGroupPayload> Groups,
    string PreviewUrl,
    bool IsConfirmed,
    bool Cached);
