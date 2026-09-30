namespace Vectorify.Api.Options;

/// <summary>
/// Configuración de la persistencia en disco del registro de versiones del
/// layout de capas (order/visible/locked)
/// (<see cref="Vectorify.Api.LayerLayout.PersistentLayerLayoutVersionRegistry"/>).
/// Se enlaza desde la sección "LayerLayoutRegistry". Mismo patrón que
/// <see cref="ManufacturingOperationRegistryOptions"/>.
/// </summary>
public sealed class LayerLayoutRegistryOptions
{
    public const string SectionName = "LayerLayoutRegistry";

    /// <summary>
    /// Carpeta donde se guarda un sidecar JSON por cada paleta+versión
    /// confirmada con layout guardado (App_Data/layer-layout/{ProjectId}/{ImageId}/{PaletteId}/{PaletteVersion}.json
    /// por default). Relativa a ContentRootPath salvo que sea una ruta absoluta.
    /// </summary>
    public string RootPath { get; set; } = "App_Data/layer-layout";
}
