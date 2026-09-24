namespace Vectify.Api.Vectorization;

/// <summary>
/// Historial de versiones de vectorización por imagen: versiona cada
/// vectorización guardada y permite referenciar/cachear el SVG generado para
/// una combinación de (máscara de origen, parámetros) ya vista. Mismo patrón
/// que IThresholdConfigRegistry (M1-S04), aplicado a esta etapa.
/// </summary>
public interface IVectorVersionRegistry
{
    /// <summary>Busca una vectorización ya generada para exactamente esta máscara de origen y estos parámetros (cache hit).</summary>
    VectorVersion? FindByParams(Guid projectId, Guid imageId, Guid sourceMaskId, VectorParameters parameters);

    /// <summary>Última versión de vectorización guardada para la imagen, o null si nunca se generó una.</summary>
    VectorVersion? FindLatest(Guid projectId, Guid imageId);

    /// <summary>Busca un registro por su vectorId, para servir los bytes del SVG.</summary>
    VectorVersion? FindByVectorId(Guid projectId, Guid imageId, Guid vectorId);

    /// <summary>
    /// Próximo número de versión para esta imagen (empieza en 1 y crece
    /// monótonamente con cada vectorización nueva, ver <see cref="Save"/>).
    /// Historial independiente del de threshold/preprocesamiento: es una
    /// etapa distinta del pipeline.
    /// </summary>
    int NextVersion(Guid projectId, Guid imageId);

    /// <summary>Guarda (o sobrescribe) un registro, indexado por proyecto/imagen, máscara de origen, parámetros y vectorId.</summary>
    void Save(VectorVersion record);
}
