namespace Vectify.Api.Threshold;

/// <summary>
/// Historial de configuraciones de threshold por imagen: versiona cada
/// configuración guardada y permite referenciar/cachear la máscara generada
/// para una combinación de (preview de origen, parámetros) ya vista. Mismo
/// patrón que IPreprocessConfigRegistry (M1-S03), aplicado a esta etapa.
/// </summary>
public interface IThresholdConfigRegistry
{
    /// <summary>Busca una máscara ya generada para exactamente este preview de origen y estos parámetros (cache hit).</summary>
    ThresholdConfigRecord? FindByParams(Guid projectId, Guid imageId, Guid sourcePreviewId, ThresholdParameters parameters);

    /// <summary>Última configuración de threshold guardada para la imagen, o null si nunca se generó una máscara.</summary>
    ThresholdConfigRecord? FindLatest(Guid projectId, Guid imageId);

    /// <summary>Busca un registro por su maskId, para servir los bytes de la máscara.</summary>
    ThresholdConfigRecord? FindByMaskId(Guid projectId, Guid imageId, Guid maskId);

    /// <summary>
    /// Próximo número de versión para esta imagen (empieza en 1 y crece
    /// monótonamente con cada configuración nueva, ver <see cref="Save"/>).
    /// Historial independiente del de preprocesamiento (M1-S03): son etapas
    /// distintas del pipeline.
    /// </summary>
    int NextVersion(Guid projectId, Guid imageId);

    /// <summary>Guarda (o sobrescribe) un registro, indexado por proyecto/imagen, preview de origen, parámetros y maskId.</summary>
    void Save(ThresholdConfigRecord record);
}
