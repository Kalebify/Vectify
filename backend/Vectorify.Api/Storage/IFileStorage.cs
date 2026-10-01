namespace Vectorify.Api.Storage;

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

    /// <summary>
    /// Borra el contenido guardado bajo la clave dada (M2.2-S04, agregado para que
    /// <c>AssetService</c> pueda hacer hard-delete de un Asset individual -- el flujo
    /// clásico nunca borra un original, por eso esta operación no existía hasta ahora).
    /// Idempotente a propósito, mismo criterio que el resto de la interfaz: si la clave ya
    /// no existe, NO lanza -- el resultado deseado ("esta clave no tiene contenido") ya se
    /// cumple.
    /// </summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken);
}

/// <summary>
/// Metadatos devueltos tras guardar un archivo exitosamente. <see cref="Checksum"/>
/// (SHA-256 en hexadecimal minúscula, M2.2-S04) se calcula DURANTE <see cref="IFileStorage.SaveAsync"/>
/// con un <see cref="System.Security.Cryptography.CryptoStream"/> mientras se copia al
/// destino -- nunca leyendo el archivo una segunda vez.
/// </summary>
public sealed record StoredFile(string Key, long SizeBytes, string Checksum);

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
