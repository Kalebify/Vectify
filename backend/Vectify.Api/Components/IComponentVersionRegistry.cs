namespace Vectify.Api.Components;

/// <summary>
/// Historial de versiones del análisis de componentes por VectorId
/// (capa): versiona cada cálculo guardado y permite referenciar/cachear el
/// análisis ya calculado para exactamente ese VectorId (que por ser
/// inmutable, alcanza como única clave de caché -- ver
/// <see cref="ComponentSetVersion"/>). Mismo patrón que
/// IVectorLayerSetRegistry (M2-S02), aplicado a esta etapa: sin un método
/// "por params" separado de "latest", porque acá son la misma consulta.
/// </summary>
public interface IComponentVersionRegistry
{
    /// <summary>Busca el análisis de componentes ya calculado para exactamente este VectorId (cache hit / última versión vigente).</summary>
    ComponentSetVersion? FindByVectorId(Guid projectId, Guid imageId, Guid vectorId);

    /// <summary>Próximo número de versión para esta imagen (empieza en 1, historial independiente del de vectorización/capas).</summary>
    int NextVersion(Guid projectId, Guid imageId);

    /// <summary>Guarda (o sobrescribe) un registro, indexado por proyecto/imagen/VectorId.</summary>
    void Save(ComponentSetVersion record);
}
