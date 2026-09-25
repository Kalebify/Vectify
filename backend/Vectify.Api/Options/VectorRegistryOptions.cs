namespace Vectify.Api.Options;

/// <summary>
/// Configuración de la persistencia en disco del registro de versiones de
/// vectorización (<see cref="Vectify.Api.Vectorization.PersistentVectorVersionRegistry"/>).
/// Se enlaza desde la sección "VectorRegistry" de appsettings/variables de entorno (por
/// ejemplo, VectorRegistry__RootPath). Mismo patrón que <see cref="ProjectRegistryOptions"/>.
/// </summary>
public sealed class VectorRegistryOptions
{
    public const string SectionName = "VectorRegistry";

    /// <summary>
    /// Carpeta donde se guarda un sidecar JSON por cada <see cref="Vectify.Api.Vectorization.VectorVersion"/>
    /// (App_Data/vectors/{ProjectId}/{ImageId}/{VectorId}.json por default), para que el
    /// historial sobreviva a un reinicio del proceso sin una base de datos de negocio.
    /// Relativa a ContentRootPath salvo que sea una ruta absoluta. Nunca se versiona (ver .gitignore).
    /// </summary>
    public string RootPath { get; set; } = "App_Data/vectors";
}
