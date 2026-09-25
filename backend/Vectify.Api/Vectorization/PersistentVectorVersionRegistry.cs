using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Vectify.Api.Options;

namespace Vectify.Api.Vectorization;

/// <summary>
/// Implementación de <see cref="IVectorVersionRegistry"/> registrada como singleton
/// (igual que InMemoryVectorVersionRegistry), pero que además persiste cada
/// <see cref="VectorVersion"/> como un sidecar JSON en disco
/// (App_Data/vectors/{ProjectId}/{ImageId}/{VectorId}.json por default, configurable vía
/// <see cref="VectorRegistryOptions"/>), con escritura atómica vía temp+move -- mismo
/// patrón exacto que <see cref="Vectify.Api.Projects.PersistentProjectRegistry"/> y
/// <see cref="Vectify.Api.Threshold.PersistentThresholdConfigRegistry"/> (Defecto 2 de la
/// ronda de QA sobre M1-S05/M1-S06: el historial de vectorización se perdía al reiniciar
/// el proceso). Al construirse rehidrata los diccionarios en memoria escaneando esos
/// sidecars (incluido el contador de versión por imagen, tomando el máximo Version
/// visto), así que las lecturas siguen siendo O(1) en memoria pero la fuente de verdad
/// sobrevive a un reinicio del proceso.
/// </summary>
public sealed class PersistentVectorVersionRegistry : IVectorVersionRegistry
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId), int> _versions = new();
    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId, Guid SourceMaskId, string ParamsKey), VectorVersion> _byParams = new();
    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId), VectorVersion> _latest = new();
    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId, Guid VectorId), VectorVersion> _byVectorId = new();
    private readonly string _rootPath;
    private readonly ILogger<PersistentVectorVersionRegistry> _logger;

    public PersistentVectorVersionRegistry(
        IOptions<VectorRegistryOptions> options,
        IHostEnvironment environment,
        ILogger<PersistentVectorVersionRegistry> logger)
    {
        var configuredPath = options.Value.RootPath;
        _rootPath = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(environment.ContentRootPath, configuredPath);
        _logger = logger;

        Directory.CreateDirectory(_rootPath);
        LoadFromDisk();
    }

    public VectorVersion? FindByParams(Guid projectId, Guid imageId, Guid sourceMaskId, VectorParameters parameters) =>
        _byParams.GetValueOrDefault((projectId, imageId, sourceMaskId, parameters.ToCacheKey()));

    public VectorVersion? FindLatest(Guid projectId, Guid imageId) =>
        _latest.GetValueOrDefault((projectId, imageId));

    public VectorVersion? FindByVectorId(Guid projectId, Guid imageId, Guid vectorId) =>
        _byVectorId.GetValueOrDefault((projectId, imageId, vectorId));

    public int NextVersion(Guid projectId, Guid imageId) =>
        _versions.AddOrUpdate((projectId, imageId), addValueFactory: _ => 1, updateValueFactory: (_, current) => current + 1);

    public void Save(VectorVersion record)
    {
        var imageKey = (record.ProjectId, record.ImageId);
        _byParams[(record.ProjectId, record.ImageId, record.SourceMaskId, record.Parameters.ToCacheKey())] = record;
        _latest[imageKey] = record;
        _byVectorId[(record.ProjectId, record.ImageId, record.VectorId)] = record;

        Persist(record);
    }

    /// <summary>
    /// Escribe el sidecar JSON del record a un archivo temporal y lo mueve al destino
    /// final (mismo criterio que PersistentProjectRegistry.Persist). Si la escritura
    /// falla, el record queda igual disponible en memoria hasta el próximo reinicio.
    /// </summary>
    private void Persist(VectorVersion record)
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
                "No se pudo persistir en disco la versión de vectorización {ProjectId}/{ImageId}/{VectorId}; sigue disponible en memoria hasta el próximo reinicio del proceso.",
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
                var record = JsonSerializer.Deserialize<VectorVersion>(json, SerializerOptions);
                if (record is null)
                {
                    continue;
                }

                var imageKey = (record.ProjectId, record.ImageId);
                _byParams[(record.ProjectId, record.ImageId, record.SourceMaskId, record.Parameters.ToCacheKey())] = record;
                _byVectorId[(record.ProjectId, record.ImageId, record.VectorId)] = record;

                if (!_latest.TryGetValue(imageKey, out var currentLatest) || record.Version > currentLatest.Version)
                {
                    _latest[imageKey] = record;
                }

                _versions.AddOrUpdate(imageKey, addValueFactory: _ => record.Version, updateValueFactory: (_, current) => Math.Max(current, record.Version));

                loaded++;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "No se pudo leer/parsear el sidecar de vectorización '{File}'; se lo ignora.", file);
            }
        }

        _logger.LogInformation(
            "PersistentVectorVersionRegistry rehidratado con {Count} versión(es) desde '{RootPath}'.",
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
