namespace Vectify.Api.Storage;

/// <summary>
/// Abstrae dónde y cómo se guardan los originales subidos. Las claves son lógicas
/// (no paths de filesystem), por ejemplo "{projectId}/{imageId}/original.png", para
/// que el contrato sea igual de válido sobre disco local que sobre un backend
/// S3-compatible. <see cref="LocalFileStorage"/> es la única implementación de este
/// sprint (desarrollo); una implementación S3-compatible se agrega en un sprint futuro
/// sin tocar quien consume esta interfaz.
/// </summary>
public interface IFileStorage
{
    /// <summary>
    /// Guarda el contenido bajo la clave dada. El original nunca se modifica después
    /// de guardado: cada clave se escribe una sola vez.
    /// </summary>
    Task<StoredFile> SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken);

    /// <summary>Abre el contenido guardado bajo la clave dada para lectura.</summary>
    /// <exception cref="FileNotFoundException">No existe contenido guardado con esa clave.</exception>
    Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken);

    /// <summary>Indica si existe contenido guardado bajo la clave dada.</summary>
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken);
}

/// <summary>Metadatos devueltos tras guardar un archivo exitosamente.</summary>
public sealed record StoredFile(string Key, long SizeBytes);

/// <summary>
/// Fallo de la capa de almacenamiento (disco lleno, permisos, I/O, etc.) — se
/// traduce en la Web API al error controlado "storage_failure".
/// </summary>
public sealed class FileStorageException : Exception
{
    public FileStorageException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
