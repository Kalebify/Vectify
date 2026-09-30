namespace Vectorify.Api.LayerLayout;

/// <summary>
/// Una versión guardada del CONJUNTO de metadata de layout (order/visible/
/// locked) de TODAS las capas de una paleta confirmada (M2-S02) -- M2.1-S07.
/// Mismo patrón inmutable EXACTO que
/// <see cref="Vectorify.Api.ManufacturingOperations.ManufacturingOperationSetVersion"/>
/// (el precedente más cercano -- mismo tipo de metadata: una entrada por
/// capa, versión COMPLETA nueva en cada cambio, nunca se muta una
/// existente): la clave de "sesión" es (<see cref="PaletteId"/>,
/// <see cref="PaletteVersion"/>), la MISMA combinación con la que
/// <see cref="Vectorify.Api.VectorLayers.IVectorLayerSetRegistry.FindByParams"/>
/// cachea un conjunto de capas ya generado. <see cref="LayerSetId"/> se
/// guarda solo para trazabilidad/depuración, NO forma parte de la clave de
/// búsqueda. Si la paleta se recalcula (una confirmación nueva que produce un
/// PaletteVersion distinto), el layout viejo simplemente no existe para esa
/// combinación nueva -- no hace falta ninguna lógica de migración explícita,
/// mismo criterio ya establecido por M2-S05 (ComponentGroup/ComponentSetVersion)
/// y M2-S07 (ManufacturingOperationSetVersion).
/// </summary>
public sealed record LayerLayoutSetVersion(
    Guid ProjectId,
    Guid ImageId,
    Guid PaletteId,
    int PaletteVersion,
    Guid LayerSetId,
    int Version,
    IReadOnlyList<LayerLayoutEntry> Entries,
    DateTimeOffset CreatedAt);
