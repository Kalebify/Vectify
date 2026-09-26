namespace Vectify.Api.VectorLayers;

/// <summary>
/// Una versión guardada del CONJUNTO COMPLETO de capas de una paleta de
/// colores confirmada (M2-S02): historial inmutable, mismo patrón que
/// VectorVersion/ColorPaletteVersion -- ninguna operación muta una versión
/// existente, cada regeneración crea la SIGUIENTE (Version+1) bajo el mismo
/// <see cref="PaletteId"/> (identidad estable de la sesión de paleta,
/// heredada de <see cref="Vectify.Api.ColorPalette.ColorPaletteVersion"/>).
///
/// <see cref="PaletteVersion"/> es la versión CONFIRMADA de la paleta que
/// generó este conjunto: junto con <see cref="PaletteId"/> forma la clave de
/// caché (no hay otros parámetros ajustables en este sprint, ver
/// Vectify.Api.Vectorization.VectorParameters) -- misma paleta+versión
/// confirmada ya vista -> cache-hit (reutiliza las capas ya generadas, pero
/// igual avanza <see cref="Version"/>, nunca retrocede ni muta una versión
/// existente, mismo criterio que el resto del pipeline).
/// </summary>
public sealed record VectorLayerSetVersion(
    Guid ProjectId,
    Guid ImageId,
    int Version,
    Guid LayerSetId,
    Guid PaletteId,
    int PaletteVersion,
    IReadOnlyList<VectorLayer> Layers,
    int SourceWidthPx,
    int SourceHeightPx,
    DateTimeOffset CreatedAt);
