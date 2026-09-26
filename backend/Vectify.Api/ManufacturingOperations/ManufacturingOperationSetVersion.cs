namespace Vectify.Api.ManufacturingOperations;

/// <summary>
/// Una versión guardada del CONJUNTO de asignaciones de operación de
/// fabricación de TODAS las capas de una paleta confirmada (M2-S02):
/// asignar/reasignar una capa crea SIEMPRE una versión nueva de este
/// conjunto, nunca muta una existente -- mismo patrón inmutable que el resto
/// del pipeline (ver <see cref="Vectify.Api.Components.ComponentGroupSetVersion"/>,
/// el precedente más cercano).
///
/// La clave de "sesión" es (<see cref="PaletteId"/>, <see cref="PaletteVersion"/>):
/// exactamente la misma combinación con la que
/// <see cref="Vectify.Api.VectorLayers.IVectorLayerSetRegistry.FindByParams"/>
/// cachea un conjunto de capas ya generado. Si M2-S02 recalcula (una
/// confirmación nueva de la paleta que produce un PaletteVersion distinto),
/// las asignaciones viejas simplemente no existen para esa combinación nueva
/// -- no hace falta ninguna lógica de migración explícita, el mismo criterio
/// de "no migrar automáticamente" que ya usa
/// Vectify.Api.Components.ComponentGroup/ComponentSetVersion (M2-S05) surge
/// naturalmente de la clave. <see cref="LayerSetId"/> se guarda solo para
/// trazabilidad/depuración (identifica exactamente qué VectorLayerSetVersion
/// estaba vigente cuando se guardó esta versión de asignaciones), NO forma
/// parte de la clave de búsqueda.
/// </summary>
public sealed record ManufacturingOperationSetVersion(
    Guid ProjectId,
    Guid ImageId,
    Guid PaletteId,
    int PaletteVersion,
    Guid LayerSetId,
    int Version,
    IReadOnlyList<ManufacturingOperationAssignment> Assignments,
    DateTimeOffset CreatedAt);
