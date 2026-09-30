using Vectorify.Api.VectorLayers;

namespace Vectorify.Api.LayerLayout;

/// <summary>
/// Orquesta la persistencia interactiva del layout de capas (M2.1-S07):
/// visibilidad (Eye), bloqueo de edición (Lock) y orden visual (Drag &amp;
/// Drop) -- lo que M2.1-S03 dejó explícitamente pendiente para "una tarjeta
/// posterior". Pura metadata sobre el conjunto de capas YA generado por
/// M2-S02 -- sin ninguna llamada a Python ni a storage de vectores, sin tocar
/// geometría, sin crear una VectorVersion/VectorLayerSetVersion nueva. Cada
/// mutación exitosa crea un <see cref="LayerLayoutSetVersion"/> NUEVO --
/// nunca muta uno existente.
/// </summary>
public interface ILayerLayoutService
{
    /// <summary>Persiste la visibilidad (Eye) de UNA capa. Togglear Visible NO afecta Locked ni Order.</summary>
    Task<LayerLayoutResult> SetVisibleAsync(
        Guid projectId, Guid imageId, Guid paletteId, Guid groupId, bool visible, CancellationToken cancellationToken);

    /// <summary>Persiste el bloqueo de edición (Lock) de UNA capa. Togglear Locked NO afecta Visible ni Order -- una capa bloqueada sigue siendo visible/seleccionable/inspeccionable.</summary>
    Task<LayerLayoutResult> SetLockedAsync(
        Guid projectId, Guid imageId, Guid paletteId, Guid groupId, bool locked, CancellationToken cancellationToken);

    /// <summary>
    /// Persiste el nuevo orden visual de TODAS las capas del conjunto vigente
    /// (Drag &amp; Drop): <paramref name="orderedGroupIds"/> debe contener,
    /// exactamente una vez cada uno, todos los groupId de
    /// <see cref="IVectorLayerService.FindLatest"/> -- si no, ValidationFailed.
    /// NUNCA toca <c>d</c>/transform/geometría: solo reescribe
    /// <see cref="LayerLayoutEntry.Order"/>, preservando Visible/Locked de
    /// cada capa intactos.
    /// </summary>
    Task<LayerLayoutResult> ReorderAsync(
        Guid projectId, Guid imageId, Guid paletteId, IReadOnlyList<Guid> orderedGroupIds, CancellationToken cancellationToken);

    /// <summary>
    /// Recupera, para el conjunto de capas ACTUAL de esa paleta, el layout
    /// vigente (o null si esa paleta nunca generó capas). El layout devuelto
    /// puede ser null aunque el LayerSet exista -- ver
    /// <see cref="LayerLayoutDefaults.Resolve"/> para los valores DEFAULT que
    /// aplican en ese caso.
    /// </summary>
    (VectorLayerSetVersion LayerSet, LayerLayoutSetVersion? Layout)? FindCurrent(
        Guid projectId, Guid imageId, Guid paletteId);
}
