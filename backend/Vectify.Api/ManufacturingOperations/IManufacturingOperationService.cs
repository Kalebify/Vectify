namespace Vectify.Api.ManufacturingOperations;

/// <summary>
/// Orquesta la asignación de intención de fabricación (Corte/Grabado/
/// Ignorar) por capa de color (M2-S07): pura metadata sobre el conjunto de
/// capas YA generado por M2-S02 -- sin ninguna llamada a Python ni a
/// storage, sin tocar geometría, sin crear una VectorVersion/
/// VectorLayerSetVersion nueva. Cada asignación exitosa crea una
/// <see cref="ManufacturingOperationSetVersion"/> NUEVA -- nunca muta una
/// existente.
/// </summary>
public interface IManufacturingOperationService
{
    /// <summary>
    /// Asigna (o reasigna) la operación de fabricación de UNA capa
    /// (<paramref name="groupId"/>) dentro del conjunto de capas vigente de
    /// <paramref name="paletteId"/>. Validado contra las capas de la última
    /// <see cref="Vectify.Api.VectorLayers.VectorLayerSetVersion"/> de esa
    /// paleta (el groupId debe existir ahí). Reasignar una capa ya asignada
    /// reemplaza SOLO esa entrada, preservando las demás asignaciones
    /// intactas, y avanza la versión del conjunto completo.
    /// </summary>
    Task<ManufacturingOperationResult> AssignAsync(
        Guid projectId, Guid imageId, Guid paletteId, Guid groupId, string? operation, CancellationToken cancellationToken);

    /// <summary>
    /// Recupera la última versión vigente del conjunto de asignaciones para
    /// el conjunto de capas ACTUAL de esa paleta (resuelto vía
    /// <see cref="Vectify.Api.VectorLayers.IVectorLayerService.FindLatest"/>),
    /// o null si esa paleta nunca generó capas. Si el conjunto de capas se
    /// regeneró desde la última vez que se asignó algo (PaletteVersion
    /// distinto), esto devuelve null incluso si existen asignaciones viejas
    /// para una PaletteVersion anterior -- no se migran automáticamente (ver
    /// spec.md, mismo criterio que M2-S05 ComponentGroup/ComponentSetVersion).
    /// </summary>
    (Vectify.Api.VectorLayers.VectorLayerSetVersion LayerSet, ManufacturingOperationSetVersion? Assignments)? FindCurrent(
        Guid projectId, Guid imageId, Guid paletteId);
}
