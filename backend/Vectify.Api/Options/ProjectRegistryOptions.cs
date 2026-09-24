namespace Vectify.Api.Options;

/// <summary>
/// Configuración de la persistencia en disco del registro de proyectos
/// (<see cref="Vectify.Api.Projects.PersistentProjectRegistry"/>). Se enlaza desde la
/// sección "ProjectRegistry" de appsettings/variables de entorno (por ejemplo,
/// ProjectRegistry__RootPath).
/// </summary>
public sealed class ProjectRegistryOptions
{
    public const string SectionName = "ProjectRegistry";

    /// <summary>
    /// Carpeta donde se guarda un sidecar JSON por cada ProjectRecord
    /// (App_Data/projects/{ProjectId}/{ImageId}.json por default), para que la
    /// metadata sobreviva a un reinicio del proceso sin una base de datos de negocio
    /// (ver Fuera de alcance de M1-S01/M1-S02). Relativa a ContentRootPath salvo que
    /// sea una ruta absoluta. Nunca se versiona (ver .gitignore).
    /// </summary>
    public string RootPath { get; set; } = "App_Data/projects";
}
