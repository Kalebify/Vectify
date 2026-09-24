using Microsoft.Extensions.Options;
using Vectify.Api.Options;

namespace Vectify.Api.Storage;

/// <summary>
/// Implementación de <see cref="IFileStorage"/> sobre el filesystem local, pensada
/// para desarrollo (ver DoD/Fuera de alcance de M1-S02: "no almacenamiento cloud
/// productivo"). Escribe primero a un archivo temporal y lo mueve al destino final
/// para que una carga interrumpida o un fallo de I/O nunca deje un original a medio
/// escribir bajo su clave final.
/// </summary>
public sealed class LocalFileStorage : IFileStorage
{
    private readonly string _rootPath;
    private readonly ILogger<LocalFileStorage> _logger;

    public LocalFileStorage(
        IOptions<LocalStorageOptions> options,
        IHostEnvironment environment,
        ILogger<LocalFileStorage> logger)
    {
        var configuredPath = options.Value.RootPath;
        _rootPath = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(environment.ContentRootPath, configuredPath);
        _logger = logger;

        try
        {
            Directory.CreateDirectory(_rootPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new FileStorageException($"No se pudo preparar el directorio de almacenamiento local '{_rootPath}'.", ex);
        }
    }

    public async Task<StoredFile> SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken)
    {
        var finalPath = ResolvePath(key);
        var tempPath = finalPath + ".tmp";

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);

            await using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await content.CopyToAsync(fileStream, cancellationToken);
            }

            File.Move(tempPath, finalPath, overwrite: true);
        }
        catch (OperationCanceledException)
        {
            CleanupTempFile(tempPath);
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CleanupTempFile(tempPath);
            _logger.LogError(ex, "Fallo de almacenamiento local al guardar la clave {Key}", key);
            throw new FileStorageException($"No se pudo guardar el archivo bajo la clave '{key}'.", ex);
        }

        var sizeBytes = new FileInfo(finalPath).Length;
        return new StoredFile(key, sizeBytes);
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken)
    {
        var path = ResolvePath(key);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"No existe un original guardado bajo la clave '{key}'.", path);
        }

        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult(stream);
    }

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken)
    {
        return Task.FromResult(File.Exists(ResolvePath(key)));
    }

    /// <summary>
    /// Traduce una clave lógica ("proyecto/imagen/original.ext") a un path absoluto
    /// dentro de la raíz de almacenamiento, rechazando segmentos "..".
    /// </summary>
    private string ResolvePath(string key)
    {
        var segments = key.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment is "." or ".." || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
        {
            throw new FileStorageException($"Clave de almacenamiento inválida: '{key}'.");
        }

        return Path.Combine([_rootPath, .. segments]);
    }

    private void CleanupTempFile(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch (IOException)
        {
            // Best effort: si no se puede limpiar el temporal, no oculta el error original.
        }
    }
}
