using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Vectify.Api.Options;

namespace Vectify.Api.Projects;

/// <summary>
/// Implementación de <see cref="IProjectRegistry"/> registrada como singleton (igual
/// que la InMemoryProjectRegistry original), pero que además persiste cada
/// <see cref="ProjectRecord"/> como un sidecar JSON en disco
/// (App_Data/projects/{ProjectId}/{ImageId}.json por default, configurable vía
/// <see cref="ProjectRegistryOptions"/>), con escritura atómica vía temp+move (mismo
/// patrón que <see cref="Vectify.Api.Storage.LocalFileStorage"/>). Al construirse
/// rehidrata el diccionario en memoria escaneando esos sidecars, así que las lecturas
/// (<see cref="Find"/>/<see cref="FindByIdempotencyKey"/>) siguen siendo O(1) en
/// memoria pero la fuente de verdad sobrevive a un reinicio del proceso. Sigue sin
/// haber una base de datos de negocio real (fuera de alcance de este sprint, ver
/// spec.md de M1-S01/M1-S02).
/// </summary>
public sealed class PersistentProjectRegistry : IProjectRegistry
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId), ProjectRecord> _byId = new();
    private readonly ConcurrentDictionary<string, ProjectRecord> _byIdempotencyKey = new(StringComparer.Ordinal);
    private readonly string _rootPath;
    private readonly ILogger<PersistentProjectRegistry> _logger;

    public PersistentProjectRegistry(
        IOptions<ProjectRegistryOptions> options,
        IHostEnvironment environment,
        ILogger<PersistentProjectRegistry> logger)
    {
        var configuredPath = options.Value.RootPath;
        _rootPath = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(environment.ContentRootPath, configuredPath);
        _logger = logger;

        Directory.CreateDirectory(_rootPath);
        LoadFromDisk();
    }

    public void Save(ProjectRecord record)
    {
        _byId[(record.ProjectId, record.ImageId)] = record;

        if (!string.IsNullOrWhiteSpace(record.IdempotencyKey))
        {
            _byIdempotencyKey[record.IdempotencyKey] = record;
        }

        Persist(record);
    }

    public ProjectRecord? Find(Guid projectId, Guid imageId) =>
        _byId.GetValueOrDefault((projectId, imageId));

    public ProjectRecord? FindByIdempotencyKey(string idempotencyKey) =>
        string.IsNullOrWhiteSpace(idempotencyKey) ? null : _byIdempotencyKey.GetValueOrDefault(idempotencyKey);

    /// <summary>
    /// Escribe el sidecar JSON del record a un archivo temporal y lo mueve al destino
    /// final, para que un fallo de I/O a mitad de camino nunca deje un sidecar a medio
    /// escribir bajo su nombre final. Si la escritura falla, el record queda igual
    /// disponible en memoria hasta el próximo reinicio (best-effort, igual criterio
    /// que el resto de la persistencia de este sprint).
    /// </summary>
    private void Persist(ProjectRecord record)
    {
        var projectDir = Path.Combine(_rootPath, record.ProjectId.ToString("N"));
        var finalPath = Path.Combine(projectDir, $"{record.ImageId:N}.json");
        var tempPath = finalPath + ".tmp";

        try
        {
            Directory.CreateDirectory(projectDir);
            File.WriteAllText(tempPath, JsonSerializer.Serialize(record, SerializerOptions));
            File.Move(tempPath, finalPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(
                ex,
                "No se pudo persistir en disco la metadata del proyecto {ProjectId}/{ImageId}; sigue disponible en memoria hasta el próximo reinicio del proceso.",
                record.ProjectId,
                record.ImageId);
            CleanupTempFile(tempPath);
        }
    }

    private void LoadFromDisk()
    {
        if (!Directory.Exists(_rootPath))
        {
            return;
        }

        var loaded = 0;
        foreach (var file in Directory.EnumerateFiles(_rootPath, "*.json", SearchOption.AllDirectories))
        {
            try
            {
                var json = File.ReadAllText(file);
                var record = JsonSerializer.Deserialize<ProjectRecord>(json, SerializerOptions);
                if (record is null)
                {
                    continue;
                }

                _byId[(record.ProjectId, record.ImageId)] = record;
                if (!string.IsNullOrWhiteSpace(record.IdempotencyKey))
                {
                    _byIdempotencyKey[record.IdempotencyKey] = record;
                }

                loaded++;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "No se pudo leer/parsear el sidecar de proyecto '{File}'; se lo ignora.", file);
            }
        }

        _logger.LogInformation(
            "PersistentProjectRegistry rehidratado con {Count} proyecto(s) desde '{RootPath}'.",
            loaded,
            _rootPath);
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
