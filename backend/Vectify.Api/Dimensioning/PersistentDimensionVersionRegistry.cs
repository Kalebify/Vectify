using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Vectify.Api.Options;

namespace Vectify.Api.Dimensioning;

/// <summary>
/// Implementación de <see cref="IDimensionVersionRegistry"/> registrada como
/// singleton, que persiste cada <see cref="DimensionVersion"/> como un
/// sidecar JSON en disco (App_Data/dimensions/{ProjectId}/{ImageId}/{DimensionId}.json
/// por default, configurable vía <see cref="DimensionRegistryOptions"/>), con
/// escritura atómica vía temp+move -- mismo patrón exacto que
/// <see cref="Vectify.Api.Simplification.PersistentSimplificationVersionRegistry"/>.
/// Al construirse rehidrata los diccionarios en memoria escaneando esos
/// sidecars (incluido el contador de versión por imagen, tomando el máximo
/// Version visto), así que las lecturas siguen siendo O(1) en memoria pero la
/// fuente de verdad sobrevive a un reinicio del proceso -- cumple el
/// Definition of Done de spec.md ("el SVG exportable... reabre
/// conservándolas") también a nivel del propio historial de la Web API, no
/// solo del archivo SVG servido.
/// </summary>
public sealed class PersistentDimensionVersionRegistry : IDimensionVersionRegistry
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId), int> _versions = new();
    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId, Guid SourceId, DimensionSourceKind SourceKind, string ParamsKey), DimensionVersion> _byParams = new();
    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId, Guid DimensionId), DimensionVersion> _byDimensionId = new();
    private readonly string _rootPath;
    private readonly ILogger<PersistentDimensionVersionRegistry> _logger;

    public PersistentDimensionVersionRegistry(
        IOptions<DimensionRegistryOptions> options,
        IHostEnvironment environment,
        ILogger<PersistentDimensionVersionRegistry> logger)
    {
        var configuredPath = options.Value.RootPath;
        _rootPath = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(environment.ContentRootPath, configuredPath);
        _logger = logger;

        Directory.CreateDirectory(_rootPath);
        LoadFromDisk();
    }

    public DimensionVersion? FindByParams(
        Guid projectId, Guid imageId, Guid sourceId, DimensionSourceKind sourceKind, DimensionParameters parameters) =>
        _byParams.GetValueOrDefault((projectId, imageId, sourceId, sourceKind, parameters.ToCacheKey()));

    public DimensionVersion? FindByDimensionId(Guid projectId, Guid imageId, Guid dimensionId) =>
        _byDimensionId.GetValueOrDefault((projectId, imageId, dimensionId));

    public int NextVersion(Guid projectId, Guid imageId) =>
        _versions.AddOrUpdate((projectId, imageId), addValueFactory: _ => 1, updateValueFactory: (_, current) => current + 1);

    public void Save(DimensionVersion record)
    {
        _byParams[(record.ProjectId, record.ImageId, record.SourceId, record.SourceKind, record.Parameters.ToCacheKey())] = record;
        _byDimensionId[(record.ProjectId, record.ImageId, record.DimensionId)] = record;

        Persist(record);
    }

    private void Persist(DimensionVersion record)
    {
        var imageDir = Path.Combine(_rootPath, record.ProjectId.ToString("N"), record.ImageId.ToString("N"));
        var finalPath = Path.Combine(imageDir, $"{record.DimensionId:N}.json");
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
                "No se pudo persistir en disco la versión de dimensiones {ProjectId}/{ImageId}/{DimensionId}; sigue disponible en memoria hasta el próximo reinicio del proceso.",
                record.ProjectId,
                record.ImageId,
                record.DimensionId);
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
                var record = JsonSerializer.Deserialize<DimensionVersion>(json, SerializerOptions);
                if (record is null)
                {
                    continue;
                }

                _byParams[(record.ProjectId, record.ImageId, record.SourceId, record.SourceKind, record.Parameters.ToCacheKey())] = record;
                _byDimensionId[(record.ProjectId, record.ImageId, record.DimensionId)] = record;

                var imageKey = (record.ProjectId, record.ImageId);
                _versions.AddOrUpdate(imageKey, addValueFactory: _ => record.Version, updateValueFactory: (_, current) => Math.Max(current, record.Version));

                loaded++;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "No se pudo leer/parsear el sidecar de dimensiones '{File}'; se lo ignora.", file);
            }
        }

        _logger.LogInformation(
            "PersistentDimensionVersionRegistry rehidratado con {Count} versión(es) desde '{RootPath}'.",
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
