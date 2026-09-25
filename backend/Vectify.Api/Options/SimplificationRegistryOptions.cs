namespace Vectify.Api.Options;

/// <summary>
/// Configuración de la persistencia en disco del registro de versiones de
/// simplificación (<see cref="Vectify.Api.Simplification.PersistentSimplificationVersionRegistry"/>).
/// Se enlaza desde la sección "SimplificationRegistry" de appsettings/variables de entorno
/// (por ejemplo, SimplificationRegistry__RootPath). Mismo patrón que <see cref="VectorRegistryOptions"/>.
/// </summary>
public sealed class SimplificationRegistryOptions
{
    public const string SectionName = "SimplificationRegistry";

    /// <summary>
    /// Carpeta donde se guarda un sidecar JSON por cada
    /// <see cref="Vectify.Api.Simplification.SimplificationVersion"/>
    /// (App_Data/simplifications/{ProjectId}/{ImageId}/{SimplificationId}.json por default), para
    /// que el historial sobreviva a un reinicio del proceso sin una base de datos de negocio.
    /// Relativa a ContentRootPath salvo que sea una ruta absoluta. Nunca se versiona (ver .gitignore).
    /// </summary>
    public string RootPath { get; set; } = "App_Data/simplifications";
}
