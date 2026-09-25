namespace Vectify.Api.ColorPalette;

/// <summary>
/// Historial de versiones de paleta de colores por sesión (PaletteId): una
/// imagen puede tener varias sesiones de paleta en paralelo (cada
/// POST .../color-palette/detect SIN PaletteId arranca una nueva), y cada
/// sesión tiene su propio historial de versiones inmutable -- mismo patrón
/// que ISimplificationVersionRegistry/IThresholdConfigRegistry aplicado a
/// esta etapa.
/// </summary>
public interface IColorPaletteVersionRegistry
{
    /// <summary>
    /// Busca una detección ya generada para exactamente esta sesión
    /// (PaletteId) y estos parámetros (cache hit) -- usado únicamente por
    /// ColorPaletteService.DetectAsync cuando el request referencia un
    /// PaletteId existente.
    /// </summary>
    ColorPaletteVersion? FindByParams(Guid projectId, Guid imageId, Guid paletteId, ColorPaletteParameters parameters);

    /// <summary>Última versión guardada de esta sesión, o null si no existe/nunca se detectó.</summary>
    ColorPaletteVersion? FindLatest(Guid projectId, Guid imageId, Guid paletteId);

    /// <summary>Próximo número de versión para esta sesión (empieza en 1).</summary>
    int NextVersion(Guid projectId, Guid imageId, Guid paletteId);

    /// <summary>Guarda (o sobrescribe) un registro, indexado por proyecto/imagen/sesión.</summary>
    void Save(ColorPaletteVersion record);
}
