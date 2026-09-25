namespace Vectify.Api.Dimensioning;

/// <summary>
/// Historial de versiones de dimensiones físicas aplicadas por imagen:
/// versiona cada aplicación de dimensiones y permite referenciar/cachear el
/// SVG generado para una combinación de (SVG de origen, dimensiones) ya
/// vista. Mismo patrón que ISimplificationVersionRegistry (M1-S07), aplicado
/// a esta etapa -- sin FindLatest (a diferencia de Simplification/
/// Vectorization/Threshold): nada en este sprint necesita "la última
/// dimensión aplicada" sin conocer su ID, así que se omite por YAGNI.
/// </summary>
public interface IDimensionVersionRegistry
{
    /// <summary>Busca unas dimensiones ya aplicadas para exactamente este SVG de origen y estos parámetros (cache hit).</summary>
    DimensionVersion? FindByParams(
        Guid projectId, Guid imageId, Guid sourceId, DimensionSourceKind sourceKind, DimensionParameters parameters);

    /// <summary>Busca un registro por su dimensionId, para servir los bytes del SVG.</summary>
    DimensionVersion? FindByDimensionId(Guid projectId, Guid imageId, Guid dimensionId);

    /// <summary>Próximo número de versión para esta imagen (empieza en 1, historial independiente del de vectorización/simplificación).</summary>
    int NextVersion(Guid projectId, Guid imageId);

    /// <summary>Guarda (o sobrescribe) un registro, indexado por proyecto/imagen, SVG de origen, parámetros y dimensionId.</summary>
    void Save(DimensionVersion record);
}
