using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Vectify.Api.Options;

namespace Vectify.Api.Components;

/// <summary>
/// Implementación de <see cref="IComponentGroupVersionRegistry"/> registrada
/// como singleton, que persiste cada <see cref="ComponentGroupSetVersion"/>
/// como un sidecar JSON en disco (App_Data/component-groups/{ProjectId}/{ImageId}/{VectorId}.json
/// por default, configurable vía <see cref="ComponentGroupRegistryOptions"/>),
/// con escritura atómica vía temp+move -- mismo patrón exacto que
/// <see cref="PersistentComponentVersionRegistry"/> (un sidecar por VectorId,
/// sobrescrito en cada versión nueva; el número de versión in-memory rehidrata
/// tomando el máximo Version visto al arrancar).
/// </summary>
public sealed class PersistentComponentGroupVersionRegistry : IComponentGroupVersionRegistry
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId, Guid VectorId), int> _versions = new();
    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId, Guid VectorId), ComponentGroupSetVersion> _latest = new();
    private readonly string _rootPath;
    private readonly ILogger<PersistentComponentGroupVersionRegistry> _logger;

    public PersistentComponentGroupVersionRegistry(
        IOptions<ComponentGroupRegistryOptions> options,
        IHostEnvironment environment,
        ILogger<PersistentComponentGroupVersionRegistry> logger)
    {
        var configuredPath = options.Value.RootPath;
        _rootPath = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(environment.ContentRootPath, configuredPath);
        _logger = logger;

        Directory.CreateDirectory(_rootPath);
        LoadFromDisk();
    }

    public ComponentGroupSetVersion? FindLatest(Guid projectId, Guid imageId, Guid vectorId) =>
        _latest.GetValueOrDefault((projectId, imageId, vectorId));

    public int NextVersion(Guid projectId, Guid imageId, Guid vectorId) =>
        _versions.AddOrUpdate((projectId, imageId, vectorId), addValueFactory: _ => 1, updateValueFactory: (_, current) => current + 1);

    public void Save(ComponentGroupSetVersion record)
    {
        _latest[(record.ProjectId, record.ImageId, record.VectorId)] = record;
        Persist(record);
    }

    private void Persist(ComponentGroupSetVersion record)
    {
        var imageDir = Path.Combine(_rootPath, record.ProjectId.ToString("N"), record.ImageId.ToString("N"));
        var finalPath = Path.Combine(imageDir, $"{record.VectorId:N}.json");
        var tempPath = finalPath + ".tmp";

        try
        {
            Directory.CreateDirectory(imageDir);
            File.WriteAllText(tempPath, JsonSerializer.Serialize(record, SerializerOptions));
            File.Move(tempPath, finalPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(
                ex,
                "No se pudo persistir en disco la versión de grupos de componentes {ProjectId}/{ImageId}/{VectorId}; sigue disponible en memoria hasta el próximo reinicio del proceso.",
                record.ProjectId,
                record.ImageId,
                record.VectorId);
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
                var record = JsonSerializer.Deserialize<ComponentGroupSetVersion>(json, SerializerOptions);
                if (record is null)
                {
                    continue;
                }

                _latest[(record.ProjectId, record.ImageId, record.VectorId)] = record;

                var key = (record.ProjectId, record.ImageId, record.VectorId);
                _versions.AddOrUpdate(key, addValueFactory: _ => record.Version, updateValueFactory: (_, current) => Math.Max(current, record.Version));

                loaded++;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "No se pudo leer/parsear el sidecar de grupos de componentes '{File}'; se lo ignora.", file);
            }
        }

        _logger.LogInformation(
            "PersistentComponentGroupVersionRegistry rehidratado con {Count} versión(es) desde '{RootPath}'.",
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
