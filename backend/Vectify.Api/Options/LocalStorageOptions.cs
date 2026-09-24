namespace Vectify.Api.Options;

/// <summary>
/// Configuración del almacenamiento local de desarrollo (<see cref="Vectify.Api.Storage.LocalFileStorage"/>).
/// Se enlaza desde la sección "Storage" de appsettings/variables de entorno
/// (por ejemplo, Storage__RootPath).
/// </summary>
public sealed class LocalStorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>
    /// Carpeta donde se guardan los originales subidos. Relativa a ContentRootPath
    /// salvo que sea una ruta absoluta. Nunca se versiona (ver .gitignore).
    /// </summary>
    public string RootPath { get; set; } = "App_Data/uploads";
}
