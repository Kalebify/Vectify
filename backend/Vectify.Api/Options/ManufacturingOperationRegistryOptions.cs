namespace Vectify.Api.Options;

/// <summary>
/// Configuración de la persistencia en disco del registro de versiones del
/// conjunto de asignaciones de operación de fabricación
/// (<see cref="Vectify.Api.ManufacturingOperations.PersistentManufacturingOperationVersionRegistry"/>).
/// Se enlaza desde la sección "ManufacturingOperationRegistry". Mismo patrón
/// que <see cref="ComponentGroupRegistryOptions"/>.
/// </summary>
public sealed class ManufacturingOperationRegistryOptions
{
    public const string SectionName = "ManufacturingOperationRegistry";

    /// <summary>
    /// Carpeta donde se guarda un sidecar JSON por cada paleta+versión
    /// confirmada con asignaciones (App_Data/manufacturing-operations/{ProjectId}/{ImageId}/{PaletteId}/{PaletteVersion}.json
    /// por default). Relativa a ContentRootPath salvo que sea una ruta absoluta.
    /// </summary>
    public string RootPath { get; set; } = "App_Data/manufacturing-operations";
}
