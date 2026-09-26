using Vectify.Api.Contracts;

namespace Vectify.Api.Components;

/// <summary>
/// Orquesta la agrupación LÓGICA de componentes físicos (M2-S05): agrupar,
/// desagrupar y renombrar son ediciones de metadata puras sobre la última
/// versión del conjunto de grupos de UN VectorId -- no hay ninguna llamada a
/// Python ni a storage de por medio (a diferencia de
/// <see cref="IComponentAnalysisService"/>), solo referencias a componentIds
/// YA calculados por M2-S03. Cada operación exitosa crea una
/// <see cref="ComponentGroupSetVersion"/> NUEVA -- nunca muta una existente.
/// Agrupar/desagrupar/renombrar NUNCA tocan
/// <see cref="Vectify.Api.Components.LayerComponent"/> ni su
/// <see cref="Vectify.Api.Vectorization.VectorVersion"/> de origen.
/// </summary>
public interface IComponentGroupService
{
    /// <summary>
    /// Crea un grupo nuevo que referencia los componentIds indicados,
    /// validados contra la ComponentSetVersion vigente de ese VectorId
    /// (deben existir todos, y ser al menos 2 distintos -- agrupar un único
    /// componente no tiene sentido semántico). Un componente puede ya
    /// pertenecer a otro grupo: no se valida exclusividad.
    /// </summary>
    Task<ComponentGroupResult> GroupAsync(
        Guid projectId, Guid imageId, Guid vectorId, ComponentGroupCreateRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Desagrupa (elimina de la versión vigente) el grupo indicado. Los
    /// componentes referenciados siguen existiendo individualmente exactamente
    /// como antes de agruparlos -- esta operación nunca toca LayerComponent.
    /// </summary>
    Task<ComponentGroupResult> UngroupAsync(
        Guid projectId, Guid imageId, Guid vectorId, Guid groupId, CancellationToken cancellationToken);

    /// <summary>Renombra un grupo existente en la versión vigente.</summary>
    Task<ComponentGroupResult> RenameAsync(
        Guid projectId, Guid imageId, Guid vectorId, Guid groupId, ComponentGroupRenameRequest request, CancellationToken cancellationToken);

    /// <summary>Recupera la última versión vigente del conjunto de grupos de un VectorId, o null si nunca se creó ninguno.</summary>
    ComponentGroupSetVersion? FindLatest(Guid projectId, Guid imageId, Guid vectorId);
}
