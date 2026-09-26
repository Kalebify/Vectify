using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Vectify.Api.Options;

namespace Vectify.Api.PhysicalUnion;

/// <summary>
/// Implementación de <see cref="IPhysicalUnionVersionRegistry"/> registrada
/// como singleton, que persiste cada <see cref="PhysicalUnionVersion"/> como
/// un sidecar JSON en disco (App_Data/physical-unions/{ProjectId}/{ImageId}/
/// {SourceVectorId}.json por default, configurable vía
/// <see cref="PhysicalUnionRegistryOptions"/>), con escritura atómica vía
/// temp+move -- mismo patrón exacto que
/// <see cref="Vectify.Api.Components.PersistentComponentGroupVersionRegistry"/>.
/// </summary>
public sealed class PersistentPhysicalUnionVersionRegistry : IPhysicalUnionVersionRegistry
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId, Guid SourceVectorId), int> _versions = new();
    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId, Guid SourceVectorId), PhysicalUnionVersion> _latest = new();
    private readonly string _rootPath;
    private readonly ILogger<PersistentPhysicalUnionVersionRegistry> _logger;

    public PersistentPhysicalUnionVersionRegistry(
        IOptions<PhysicalUnionRegistryOptions> options,
        IHostEnvironment environment,
        ILogger<PersistentPhysicalUnionVersionRegistry> logger)
    {
        var configuredPath = options.Value.RootPath;
        _rootPath = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(environment.ContentRootPath, configuredPath);
        _logger = logger;

        Directory.CreateDirectory(_rootPath);
        LoadFromDisk();
    }

    public PhysicalUnionVersion? FindLatest(Guid projectId, Guid imageId, Guid sourceVectorId) =>
        _latest.GetValueOrDefault((projectId, imageId, sourceVectorId));

    public int NextVersion(Guid projectId, Guid imageId, Guid sourceVectorId) =>
        _versions.AddOrUpdate((projectId, imageId, sourceVectorId), addValueFactory: _ => 1, updateValueFactory: (_, current) => current + 1);

    public void Save(PhysicalUnionVersion record)
    {
        _latest[(record.ProjectId, record.ImageId, record.SourceVectorId)] = record;
        Persist(record);
    }

    private void Persist(PhysicalUnionVersion record)
    {
        var imageDir = Path.Combine(_rootPath, record.ProjectId.ToString("N"), record.ImageId.ToString("N"));
        var finalPath = Path.Combine(imageDir, $"{record.SourceVectorId:N}.json");
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
                "No se pudo persistir en disco la versión de unión física {ProjectId}/{ImageId}/{SourceVectorId}; sigue disponible en memoria hasta el próximo reinicio del proceso.",
                record.ProjectId,
                record.ImageId,
                record.SourceVectorId);
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
                var record = JsonSerializer.Deserialize<PhysicalUnionVersion>(json, SerializerOptions);
                if (record is null)
                {
                    continue;
                }

                _latest[(record.ProjectId, record.ImageId, record.SourceVectorId)] = record;

                var key = (record.ProjectId, record.ImageId, record.SourceVectorId);
                _versions.AddOrUpdate(key, addValueFactory: _ => record.Version, updateValueFactory: (_, current) => Math.Max(current, record.Version));

                loaded++;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "No se pudo leer/parsear el sidecar de unión física '{File}'; se lo ignora.", file);
            }
        }

        _logger.LogInformation(
            "PersistentPhysicalUnionVersionRegistry rehidratado con {Count} versión(es) desde '{RootPath}'.",
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
