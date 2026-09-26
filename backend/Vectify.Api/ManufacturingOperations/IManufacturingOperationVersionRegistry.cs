namespace Vectify.Api.ManufacturingOperations;

/// <summary>
/// Historial de versiones del conjunto de asignaciones de operación de
/// fabricación, indexado por sesión de paleta+versión confirmada
/// (ProjectId/ImageId/PaletteId/PaletteVersion) -- ver
/// <see cref="ManufacturingOperationSetVersion"/> para por qué esa es la
/// clave correcta (y no LayerSetId directamente). Mismo patrón que
/// Vectify.Api.Components.IComponentGroupVersionRegistry.
/// </summary>
public interface IManufacturingOperationVersionRegistry
{
    /// <summary>Última versión vigente del conjunto de asignaciones para esa paleta+versión confirmada exacta, o null si nunca se asignó nada.</summary>
    ManufacturingOperationSetVersion? FindLatest(Guid projectId, Guid imageId, Guid paletteId, int paletteVersion);

    /// <summary>Próximo número de versión para esa paleta+versión confirmada (empieza en 1, historial independiente por PaletteVersion).</summary>
    int NextVersion(Guid projectId, Guid imageId, Guid paletteId, int paletteVersion);

    /// <summary>Guarda (o sobrescribe) un registro, indexado por proyecto/imagen/paleta/versión de paleta.</summary>
    void Save(ManufacturingOperationSetVersion record);
}
