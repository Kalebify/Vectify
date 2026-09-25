namespace Vectify.Api.Simplification;

/// <summary>
/// Historial de versiones de simplificación por imagen: versiona cada
/// simplificación APLICADA (nunca las de preview -- ver
/// SimplificationService.PreviewAsync, que no toca este registro) y permite
/// referenciar/cachear el SVG generado para una combinación de (SVG de
/// origen, parámetros) ya vista. Mismo patrón que IVectorVersionRegistry
/// (M1-S05), aplicado a esta etapa.
/// </summary>
public interface ISimplificationVersionRegistry
{
    /// <summary>Busca una simplificación ya generada para exactamente este SVG de origen y estos parámetros (cache hit).</summary>
    SimplificationVersion? FindByParams(Guid projectId, Guid imageId, Guid sourceVectorId, SimplificationParameters parameters);

    /// <summary>Última versión de simplificación guardada para la imagen, o null si nunca se aplicó una.</summary>
    SimplificationVersion? FindLatest(Guid projectId, Guid imageId);

    /// <summary>Busca un registro por su simplificationId, para servir los bytes del SVG.</summary>
    SimplificationVersion? FindBySimplificationId(Guid projectId, Guid imageId, Guid simplificationId);

    /// <summary>Próximo número de versión para esta imagen (empieza en 1, historial independiente del de vectorización).</summary>
    int NextVersion(Guid projectId, Guid imageId);

    /// <summary>Guarda (o sobrescribe) un registro, indexado por proyecto/imagen, SVG de origen, parámetros y simplificationId.</summary>
    void Save(SimplificationVersion record);
}
