using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Vectify.Api.Options;

namespace Vectify.Api.Simplification;

/// <summary>
/// Implementación de <see cref="ISimplificationVersionRegistry"/> registrada como
/// singleton, que persiste cada <see cref="SimplificationVersion"/> como un sidecar JSON en
/// disco (App_Data/simplifications/{ProjectId}/{ImageId}/{SimplificationId}.json por
/// default, configurable vía <see cref="SimplificationRegistryOptions"/>), con escritura
/// atómica vía temp+move -- mismo patrón exacto que
/// <see cref="Vectify.Api.Vectorization.PersistentVectorVersionRegistry"/> (que a su vez
/// sigue el mismo patrón que PersistentProjectRegistry/PersistentThresholdConfigRegistry).
/// Al construirse rehidrata los diccionarios en memoria escaneando esos sidecars (incluido
/// el contador de versión por imagen, tomando el máximo Version visto), así que las
/// lecturas siguen siendo O(1) en memoria pero la fuente de verdad sobrevive a un reinicio
/// del proceso.
/// </summary>
public sealed class PersistentSimplificationVersionRegistry : ISimplificationVersionRegistry
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId), int> _versions = new();
    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId, Guid SourceVectorId, string ParamsKey), SimplificationVersion> _byParams = new();
    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId), SimplificationVersion> _latest = new();
    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId, Guid SimplificationId), SimplificationVersion> _bySimplificationId = new();
    private readonly string _rootPath;
    private readonly ILogger<PersistentSimplificationVersionRegistry> _logger;

    public PersistentSimplificationVersionRegistry(
        IOptions<SimplificationRegistryOptions> options,
        IHostEnvironment environment,
        ILogger<PersistentSimplificationVersionRegistry> logger)
    {
        var configuredPath = options.Value.RootPath;
        _rootPath = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(environment.ContentRootPath, configuredPath);
        _logger = logger;

        Directory.CreateDirectory(_rootPath);
        LoadFromDisk();
    }

    public SimplificationVersion? FindByParams(Guid projectId, Guid imageId, Guid sourceVectorId, SimplificationParameters parameters) =>
        _byParams.GetValueOrDefault((projectId, imageId, sourceVectorId, parameters.ToCacheKey()));

    public SimplificationVersion? FindLatest(Guid projectId, Guid imageId) =>
        _latest.GetValueOrDefault((projectId, imageId));

    public SimplificationVersion? FindBySimplificationId(Guid projectId, Guid imageId, Guid simplificationId) =>
        _bySimplificationId.GetValueOrDefault((projectId, imageId, simplificationId));

    public int NextVersion(Guid projectId, Guid imageId) =>
        _versions.AddOrUpdate((projectId, imageId), addValueFactory: _ => 1, updateValueFactory: (_, current) => current + 1);

    public void Save(SimplificationVersion record)
    {
        var imageKey = (record.ProjectId, record.ImageId);
        _byParams[(record.ProjectId, record.ImageId, record.SourceVectorId, record.Parameters.ToCacheKey())] = record;
        _latest[imageKey] = record;
        _bySimplificationId[(record.ProjectId, record.ImageId, record.SimplificationId)] = record;

        Persist(record);
    }

    private void Persist(SimplificationVersion record)
    {
        var imageDir = Path.Combine(_rootPath, record.ProjectId.ToString("N"), record.ImageId.ToString("N"));
        var finalPath = Path.Combine(imageDir, $"{record.SimplificationId:N}.json");
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
                "No se pudo persistir en disco la versión de simplificación {ProjectId}/{ImageId}/{SimplificationId}; sigue disponible en memoria hasta el próximo reinicio del proceso.",
                record.ProjectId,
                record.ImageId,
                record.SimplificationId);
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
                var record = JsonSerializer.Deserialize<SimplificationVersion>(json, SerializerOptions);
                if (record is null)
                {
                    continue;
                }

                var imageKey = (record.ProjectId, record.ImageId);
                _byParams[(record.ProjectId, record.ImageId, record.SourceVectorId, record.Parameters.ToCacheKey())] = record;
                _bySimplificationId[(record.ProjectId, record.ImageId, record.SimplificationId)] = record;

                if (!_latest.TryGetValue(imageKey, out var currentLatest) || record.Version > currentLatest.Version)
                {
                    _latest[imageKey] = record;
                }

                _versions.AddOrUpdate(imageKey, addValueFactory: _ => record.Version, updateValueFactory: (_, current) => Math.Max(current, record.Version));

                loaded++;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "No se pudo leer/parsear el sidecar de simplificación '{File}'; se lo ignora.", file);
            }
        }

        _logger.LogInformation(
            "PersistentSimplificationVersionRegistry rehidratado con {Count} versión(es) desde '{RootPath}'.",
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
