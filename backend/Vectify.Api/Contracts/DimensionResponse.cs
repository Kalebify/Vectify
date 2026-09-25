namespace Vectify.Api.Contracts;

/// <summary>
/// Respuesta de POST .../dimensions/apply y GET .../dimensions/{id}: las
/// dimensiones físicas quedaron persistidas como una nueva DimensionVersion
/// (nunca sobrescribe la anterior) -- ver spec.md M1-S09. <see cref="SourceWidthPx"/>/
/// <see cref="SourceHeightPx"/> son el ancho/alto original en unidades
/// internas (1 unidad = 1 px de la máscara vectorizada, nunca inferido de
/// DPI/EXIF), para que React pueda mostrar la escala aplicada.
/// </summary>
public sealed record DimensionResponse(
    Guid ProjectId,
    Guid ImageId,
    Guid DimensionId,
    string SvgUrl,
    string SourceKind,
    Guid SourceId,
    int Version,
    double WidthMm,
    double HeightMm,
    bool LockAspectRatio,
    int SourceWidthPx,
    int SourceHeightPx,
    bool Cached);
