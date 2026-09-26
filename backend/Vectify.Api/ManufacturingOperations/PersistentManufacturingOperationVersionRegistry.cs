using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Vectify.Api.Options;

namespace Vectify.Api.ManufacturingOperations;

/// <summary>
/// Implementación de <see cref="IManufacturingOperationVersionRegistry"/>
/// registrada como singleton, que persiste cada
/// <see cref="ManufacturingOperationSetVersion"/> como un sidecar JSON en
/// disco (App_Data/manufacturing-operations/{ProjectId}/{ImageId}/{PaletteId}/{PaletteVersion}.json
/// por default, configurable vía <see cref="ManufacturingOperationRegistryOptions"/>),
/// con escritura atómica vía temp+move -- mismo patrón exacto que
/// <see cref="Vectify.Api.Components.PersistentComponentGroupVersionRegistry"/>
/// (un sidecar por clave, sobrescrito en cada versión nueva; el número de
/// versión in-memory rehidrata tomando el máximo Version visto al arrancar).
/// </summary>
public sealed class PersistentManufacturingOperationVersionRegistry : IManufacturingOperationVersionRegistry
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId, Guid PaletteId, int PaletteVersion), int> _versions = new();
    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId, Guid PaletteId, int PaletteVersion), ManufacturingOperationSetVersion> _latest = new();
    private readonly string _rootPath;
    private readonly ILogger<PersistentManufacturingOperationVersionRegistry> _logger;

    public PersistentManufacturingOperationVersionRegistry(
        IOptions<ManufacturingOperationRegistryOptions> options,
        IHostEnvironment environment,
        ILogger<PersistentManufacturingOperationVersionRegistry> logger)
    {
        var configuredPath = options.Value.RootPath;
        _rootPath = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(environment.ContentRootPath, configuredPath);
        _logger = logger;

        Directory.CreateDirectory(_rootPath);
        LoadFromDisk();
    }

    public ManufacturingOperationSetVersion? FindLatest(Guid projectId, Guid imageId, Guid paletteId, int paletteVersion) =>
        _latest.GetValueOrDefault((projectId, imageId, paletteId, paletteVersion));

    public int NextVersion(Guid projectId, Guid imageId, Guid paletteId, int paletteVersion) =>
        _versions.AddOrUpdate((projectId, imageId, paletteId, paletteVersion), addValueFactory: _ => 1, updateValueFactory: (_, current) => current + 1);

    public void Save(ManufacturingOperationSetVersion record)
    {
        _latest[(record.ProjectId, record.ImageId, record.PaletteId, record.PaletteVersion)] = record;
        Persist(record);
    }

    private void Persist(ManufacturingOperationSetVersion record)
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
                "No se pudo persistir en disco la versión de asignaciones de operación {ProjectId}/{ImageId}/{PaletteId} v{PaletteVersion}; sigue disponible en memoria hasta el próximo reinicio del proceso.",
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
                var record = JsonSerializer.Deserialize<ManufacturingOperationSetVersion>(json, SerializerOptions);
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
                _logger.LogWarning(ex, "No se pudo leer/parsear el sidecar de operaciones de fabricación '{File}'; se lo ignora.", file);
            }
        }

        _logger.LogInformation(
            "PersistentManufacturingOperationVersionRegistry rehidratado con {Count} versión(es) desde '{RootPath}'.",
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
