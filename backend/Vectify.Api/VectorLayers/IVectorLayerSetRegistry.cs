namespace Vectify.Api.VectorLayers;

/// <summary>
/// Historial de versiones del conjunto de capas por sesión de paleta
/// (PaletteId): versiona cada generación guardada y permite referenciar/
/// cachear el conjunto ya generado para una versión de paleta CONFIRMADA ya
/// vista. Mismo patrón que IColorPaletteVersionRegistry (M2-S01), aplicado a
/// esta etapa.
/// </summary>
public interface IVectorLayerSetRegistry
{
    /// <summary>Busca un conjunto de capas ya generado para exactamente esta paleta+versión confirmada (cache hit).</summary>
    VectorLayerSetVersion? FindByParams(Guid projectId, Guid imageId, Guid paletteId, int paletteVersion);

    /// <summary>Último conjunto de capas guardado para esta sesión de paleta, o null si nunca se generó uno.</summary>
    VectorLayerSetVersion? FindLatest(Guid projectId, Guid imageId, Guid paletteId);

    /// <summary>
    /// Próximo número de versión para esta sesión de paleta (empieza en 1 y
    /// crece monótonamente con cada generación nueva, ver <see cref="Save"/>).
    /// </summary>
    int NextVersion(Guid projectId, Guid imageId, Guid paletteId);

    /// <summary>Guarda (o sobrescribe) un registro, indexado por sesión de paleta y por paleta+versión.</summary>
    void Save(VectorLayerSetVersion record);
}
