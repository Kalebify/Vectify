namespace Vectify.Api.Options;

/// <summary>
/// Configuración de la persistencia en disco del registro de versiones de
/// dimensiones (<see cref="Vectify.Api.Dimensioning.PersistentDimensionVersionRegistry"/>).
/// Se enlaza desde la sección "DimensionsRegistry" de appsettings/variables de entorno
/// (por ejemplo, DimensionsRegistry__RootPath). Mismo patrón que <see cref="SimplificationRegistryOptions"/>.
/// </summary>
public sealed class DimensionRegistryOptions
{
    public const string SectionName = "DimensionsRegistry";

    /// <summary>
    /// Carpeta donde se guarda un sidecar JSON por cada
    /// <see cref="Vectify.Api.Dimensioning.DimensionVersion"/>
    /// (App_Data/dimensions/{ProjectId}/{ImageId}/{DimensionId}.json por default), para
    /// que el historial sobreviva a un reinicio del proceso sin una base de datos de negocio.
    /// Relativa a ContentRootPath salvo que sea una ruta absoluta. Nunca se versiona (ver .gitignore).
    /// </summary>
    public string RootPath { get; set; } = "App_Data/dimensions";
}
