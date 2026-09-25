namespace Vectify.Api.Options;

/// <summary>
/// Configuración de la persistencia en disco del registro de configuraciones de
/// threshold (<see cref="Vectify.Api.Threshold.PersistentThresholdConfigRegistry"/>). Se
/// enlaza desde la sección "ThresholdRegistry" de appsettings/variables de entorno (por
/// ejemplo, ThresholdRegistry__RootPath). Mismo patrón que <see cref="ProjectRegistryOptions"/>.
/// </summary>
public sealed class ThresholdRegistryOptions
{
    public const string SectionName = "ThresholdRegistry";

    /// <summary>
    /// Carpeta donde se guarda un sidecar JSON por cada <see cref="Vectify.Api.Threshold.ThresholdConfigRecord"/>
    /// (App_Data/thresholds/{ProjectId}/{ImageId}/{MaskId}.json por default), para que el
    /// historial sobreviva a un reinicio del proceso sin una base de datos de negocio.
    /// Relativa a ContentRootPath salvo que sea una ruta absoluta. Nunca se versiona (ver .gitignore).
    /// </summary>
    public string RootPath { get; set; } = "App_Data/thresholds";
}
