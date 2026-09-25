namespace Vectify.Api.Dimensioning;

/// <summary>
/// Una versión guardada de dimensiones físicas aplicadas a un SVG, junto con
/// la referencia al SVG ya generado (cacheado) para ese SVG de origen +
/// dimensiones, y al recurso de origen (VectorVersion o SimplificationVersion,
/// según <see cref="SourceKind"/>) del que se originó. Módulo propio y
/// paralelo a Vectorization/Simplification -- mismo patrón de historial
/// versionado (nunca se sobrescribe, cache-hit avanza la versión) que
/// VectorVersion/SimplificationVersion. <see cref="SourceWidthPx"/>/
/// <see cref="SourceHeightPx"/> son el ancho/alto ORIGINAL en unidades
/// internas (1 unidad = 1 px de la máscara vectorizada, ver
/// <see cref="SvgDimensionWriter"/>), conservados para poder mostrar la
/// escala aplicada (mm por unidad) sin tener que re-parsear el SVG.
/// </summary>
public sealed record DimensionVersion(
    Guid ProjectId,
    Guid ImageId,
    int Version,
    Guid DimensionId,
    Guid SourceId,
    DimensionSourceKind SourceKind,
    DimensionParameters Parameters,
    string SvgStorageKey,
    string ContentType,
    int SourceWidthPx,
    int SourceHeightPx,
    DateTimeOffset CreatedAt);
