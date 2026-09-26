namespace Vectify.Api.Components;

/// <summary>
/// Historial de versiones del conjunto de grupos lógicos por VectorId (capa):
/// versiona cada edición (agrupar/desagrupar/renombrar) guardada. Mismo
/// patrón que <see cref="IComponentVersionRegistry"/>, aplicado al conjunto
/// de <see cref="ComponentGroup"/> en vez de al conjunto de
/// <see cref="LayerComponent"/>.
/// </summary>
public interface IComponentGroupVersionRegistry
{
    /// <summary>Busca la última versión vigente del conjunto de grupos para exactamente este VectorId, o null si nunca se creó ninguno.</summary>
    ComponentGroupSetVersion? FindLatest(Guid projectId, Guid imageId, Guid vectorId);

    /// <summary>Próximo número de versión para este VectorId (empieza en 1, historial independiente del de componentes/vectorización).</summary>
    int NextVersion(Guid projectId, Guid imageId, Guid vectorId);

    /// <summary>Guarda (o sobrescribe) un registro, indexado por proyecto/imagen/VectorId.</summary>
    void Save(ComponentGroupSetVersion record);
}
