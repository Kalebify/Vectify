using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Vectorify.Api.Options;

namespace Vectorify.Api.LayerLayout;

/// <summary>
/// Implementación de <see cref="ILayerLayoutVersionRegistry"/> registrada
/// como singleton, que persiste cada <see cref="LayerLayoutSetVersion"/> como
/// un sidecar JSON en disco
/// (App_Data/layer-layout/{ProjectId}/{ImageId}/{PaletteId}/{PaletteVersion}.json
/// por default, configurable vía <see cref="LayerLayoutRegistryOptions"/>),
/// con escritura atómica vía temp+move -- mismo patrón exacto que
/// <see cref="Vectorify.Api.ManufacturingOperations.PersistentManufacturingOperationVersionRegistry"/>
/// (un sidecar por clave, sobrescrito en cada versión nueva; el número de
/// versión in-memory rehidrata tomando el máximo Version visto al arrancar).
/// </summary>
public sealed class PersistentLayerLayoutVersionRegistry : ILayerLayoutVersionRegistry
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId, Guid PaletteId, int PaletteVersion), int> _versions = new();
    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId, Guid PaletteId, int PaletteVersion), LayerLayoutSetVersion> _latest = new();
    private readonly string _rootPath;
    private readonly ILogger<PersistentLayerLayoutVersionRegistry> _logger;

    public PersistentLayerLayoutVersionRegistry(
        IOptions<LayerLayoutRegistryOptions> options,
        IHostEnvironment environment,
        ILogger<PersistentLayerLayoutVersionRegistry> logger)
    {
        var configuredPath = options.Value.RootPath;
        _rootPath = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(environment.ContentRootPath, configuredPath);
        _logger = logger;

        Directory.CreateDirectory(_rootPath);
        LoadFromDisk();
    }

    public LayerLayoutSetVersion? FindLatest(Guid projectId, Guid imageId, Guid paletteId, int paletteVersion) =>
        _latest.GetValueOrDefault((projectId, imageId, paletteId, paletteVersion));

    public int NextVersion(Guid projectId, Guid imageId, Guid paletteId, int paletteVersion) =>
        _versions.AddOrUpdate((projectId, imageId, paletteId, paletteVersion), addValueFactory: _ => 1, updateValueFactory: (_, current) => current + 1);

    public void Save(LayerLayoutSetVersion record)
    {
        _latest[(record.ProjectId, record.ImageId, record.PaletteId, record.PaletteVersion)] = record;
        Persist(record);
    }

    private void Persist(LayerLayoutSetVersion record)
    {
        var sessionDir = Path.Combine(
            _rootPath, record.ProjectId.ToString("N"), record.ImageId.ToString("N"), record.PaletteId.ToString("N"));
        var finalPath = Path.Combine(sessionDir, $"{record.PaletteVersion}.json");
        var tempPath = finalPath + ".tmp";

        try
        {
            Directory.CreateDirectory(sessionDir);
            File.WriteAllText(tempPath, JsonSerializer.Serialize(record, SerializerOptions));
            File.Move(tempPath, finalPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(
                ex,
                "No se pudo persistir en disco la versión de layout {ProjectId}/{ImageId}/{PaletteId} v{PaletteVersion}; sigue disponible en memoria hasta el próximo reinicio del proceso.",
                record.ProjectId,
                record.ImageId,
                record.PaletteId,
                record.PaletteVersion);
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
                var record = JsonSerializer.Deserialize<LayerLayoutSetVersion>(json, SerializerOptions);
                if (record is null)
                {
                    continue;
                }

                var key = (record.ProjectId, record.ImageId, record.PaletteId, record.PaletteVersion);
                _latest[key] = record;
                _versions.AddOrUpdate(key, addValueFactory: _ => record.Version, updateValueFactory: (_, current) => Math.Max(current, record.Version));

                loaded++;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "No se pudo leer/parsear el sidecar de layout de capas '{File}'; se lo ignora.", file);
            }
        }

        _logger.LogInformation(
            "PersistentLayerLayoutVersionRegistry rehidratado con {Count} versión(es) desde '{RootPath}'.",
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
