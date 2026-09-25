using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Vectify.Api.Options;

namespace Vectify.Api.Threshold;

/// <summary>
/// Implementación de <see cref="IThresholdConfigRegistry"/> registrada como singleton
/// (igual que InMemoryThresholdConfigRegistry), pero que además persiste cada
/// <see cref="ThresholdConfigRecord"/> como un sidecar JSON en disco
/// (App_Data/thresholds/{ProjectId}/{ImageId}/{MaskId}.json por default, configurable vía
/// <see cref="ThresholdRegistryOptions"/>), con escritura atómica vía temp+move -- mismo
/// patrón exacto que <see cref="Vectify.Api.Projects.PersistentProjectRegistry"/> (Defecto 2
/// de la ronda de QA sobre M1-S05/M1-S06: el historial de threshold se perdía al
/// reiniciar el proceso). Al construirse rehidrata los diccionarios en memoria
/// escaneando esos sidecars (incluido el contador de versión por imagen, tomando el
/// máximo Version visto), así que las lecturas siguen siendo O(1) en memoria pero la
/// fuente de verdad sobrevive a un reinicio del proceso.
/// </summary>
public sealed class PersistentThresholdConfigRegistry : IThresholdConfigRegistry
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId), int> _versions = new();
    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId, Guid SourcePreviewId, string ParamsKey), ThresholdConfigRecord> _byParams = new();
    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId), ThresholdConfigRecord> _latest = new();
    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId, Guid MaskId), ThresholdConfigRecord> _byMaskId = new();
    private readonly string _rootPath;
    private readonly ILogger<PersistentThresholdConfigRegistry> _logger;

    public PersistentThresholdConfigRegistry(
        IOptions<ThresholdRegistryOptions> options,
        IHostEnvironment environment,
        ILogger<PersistentThresholdConfigRegistry> logger)
    {
        var configuredPath = options.Value.RootPath;
        _rootPath = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(environment.ContentRootPath, configuredPath);
        _logger = logger;

        Directory.CreateDirectory(_rootPath);
        LoadFromDisk();
    }

    public ThresholdConfigRecord? FindByParams(Guid projectId, Guid imageId, Guid sourcePreviewId, ThresholdParameters parameters) =>
        _byParams.GetValueOrDefault((projectId, imageId, sourcePreviewId, parameters.ToCacheKey()));

    public ThresholdConfigRecord? FindLatest(Guid projectId, Guid imageId) =>
        _latest.GetValueOrDefault((projectId, imageId));

    public ThresholdConfigRecord? FindByMaskId(Guid projectId, Guid imageId, Guid maskId) =>
        _byMaskId.GetValueOrDefault((projectId, imageId, maskId));

    public int NextVersion(Guid projectId, Guid imageId) =>
        _versions.AddOrUpdate((projectId, imageId), addValueFactory: _ => 1, updateValueFactory: (_, current) => current + 1);

    public void Save(ThresholdConfigRecord record)
    {
        var imageKey = (record.ProjectId, record.ImageId);
        _byParams[(record.ProjectId, record.ImageId, record.SourcePreviewId, record.Parameters.ToCacheKey())] = record;
        _latest[imageKey] = record;
        _byMaskId[(record.ProjectId, record.ImageId, record.MaskId)] = record;

        Persist(record);
    }

    /// <summary>
    /// Escribe el sidecar JSON del record a un archivo temporal y lo mueve al destino
    /// final (mismo criterio que PersistentProjectRegistry.Persist). Si la escritura
    /// falla, el record queda igual disponible en memoria hasta el próximo reinicio.
    /// </summary>
    private void Persist(ThresholdConfigRecord record)
    {
        var imageDir = Path.Combine(_rootPath, record.ProjectId.ToString("N"), record.ImageId.ToString("N"));
        var finalPath = Path.Combine(imageDir, $"{record.MaskId:N}.json");
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
                "No se pudo persistir en disco la configuración de threshold {ProjectId}/{ImageId}/{MaskId}; sigue disponible en memoria hasta el próximo reinicio del proceso.",
                record.ProjectId,
                record.ImageId,
                record.MaskId);
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
                var record = JsonSerializer.Deserialize<ThresholdConfigRecord>(json, SerializerOptions);
                if (record is null)
                {
                    continue;
                }

                var imageKey = (record.ProjectId, record.ImageId);
                _byParams[(record.ProjectId, record.ImageId, record.SourcePreviewId, record.Parameters.ToCacheKey())] = record;
                _byMaskId[(record.ProjectId, record.ImageId, record.MaskId)] = record;

                if (!_latest.TryGetValue(imageKey, out var currentLatest) || record.Version > currentLatest.Version)
                {
                    _latest[imageKey] = record;
                }

                _versions.AddOrUpdate(imageKey, addValueFactory: _ => record.Version, updateValueFactory: (_, current) => Math.Max(current, record.Version));

                loaded++;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "No se pudo leer/parsear el sidecar de threshold '{File}'; se lo ignora.", file);
            }
        }

        _logger.LogInformation(
            "PersistentThresholdConfigRegistry rehidratado con {Count} configuración(es) desde '{RootPath}'.",
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
