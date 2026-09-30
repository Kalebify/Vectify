namespace Vectorify.Api.LayerLayout;

/// <summary>
/// Historial de versiones del conjunto de layout (order/visible/locked),
/// indexado por sesión de paleta+versión confirmada (ProjectId/ImageId/
/// PaletteId/PaletteVersion) -- ver <see cref="LayerLayoutSetVersion"/> para
/// por qué esa es la clave correcta. Mismo patrón que
/// Vectorify.Api.ManufacturingOperations.IManufacturingOperationVersionRegistry.
/// </summary>
public interface ILayerLayoutVersionRegistry
{
    /// <summary>Última versión vigente del conjunto de layout para esa paleta+versión confirmada exacta, o null si nunca se guardó nada.</summary>
    LayerLayoutSetVersion? FindLatest(Guid projectId, Guid imageId, Guid paletteId, int paletteVersion);

    /// <summary>Próximo número de versión para esa paleta+versión confirmada (empieza en 1, historial independiente por PaletteVersion).</summary>
    int NextVersion(Guid projectId, Guid imageId, Guid paletteId, int paletteVersion);

    /// <summary>Guarda (o sobrescribe) un registro, indexado por proyecto/imagen/paleta/versión de paleta.</summary>
    void Save(LayerLayoutSetVersion record);
}
