namespace Vectify.Api.Options;

/// <summary>
/// Configuración de la persistencia en disco del registro de versiones del
/// análisis de componentes (<see cref="Vectify.Api.Components.PersistentComponentVersionRegistry"/>).
/// Se enlaza desde la sección "ComponentRegistry". Mismo patrón que <see cref="DimensionRegistryOptions"/>.
/// </summary>
public sealed class ComponentRegistryOptions
{
    public const string SectionName = "ComponentRegistry";

    /// <summary>
    /// Carpeta donde se guarda un sidecar JSON por cada VectorId analizado
    /// (App_Data/components/{ProjectId}/{ImageId}/{VectorId}.json por
    /// default). Relativa a ContentRootPath salvo que sea una ruta absoluta.
    /// </summary>
    public string RootPath { get; set; } = "App_Data/components";
}
