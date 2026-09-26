namespace Vectify.Api.Options;

/// <summary>
/// Configuración de la persistencia en disco del registro de versiones del
/// conjunto de grupos lógicos de componentes
/// (<see cref="Vectify.Api.Components.PersistentComponentGroupVersionRegistry"/>).
/// Se enlaza desde la sección "ComponentGroupRegistry". Mismo patrón que
/// <see cref="ComponentRegistryOptions"/>.
/// </summary>
public sealed class ComponentGroupRegistryOptions
{
    public const string SectionName = "ComponentGroupRegistry";

    /// <summary>
    /// Carpeta donde se guarda un sidecar JSON por cada VectorId con grupos
    /// (App_Data/component-groups/{ProjectId}/{ImageId}/{VectorId}.json por
    /// default). Relativa a ContentRootPath salvo que sea una ruta absoluta.
    /// </summary>
    public string RootPath { get; set; } = "App_Data/component-groups";
}
