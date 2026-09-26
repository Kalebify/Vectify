namespace Vectify.Api.Options;

/// <summary>
/// Configuración de la persistencia en disco del registro de versiones del
/// conjunto de capas (<see cref="Vectify.Api.VectorLayers.PersistentVectorLayerSetRegistry"/>).
/// Se enlaza desde la sección "VectorLayerRegistry". Mismo patrón que <see cref="ColorPaletteRegistryOptions"/>.
/// </summary>
public sealed class VectorLayerRegistryOptions
{
    public const string SectionName = "VectorLayerRegistry";

    /// <summary>
    /// Carpeta donde se guarda un sidecar JSON por cada
    /// <see cref="Vectify.Api.VectorLayers.VectorLayerSetVersion"/> (App_Data/vector-layers/{ProjectId}/{ImageId}/{PaletteId}/{Version}.json
    /// por default). Relativa a ContentRootPath salvo que sea una ruta absoluta.
    /// </summary>
    public string RootPath { get; set; } = "App_Data/vector-layers";
}
