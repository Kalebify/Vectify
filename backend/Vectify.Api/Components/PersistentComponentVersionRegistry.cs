using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Vectify.Api.Options;

namespace Vectify.Api.Components;

/// <summary>
/// Implementación de <see cref="IComponentVersionRegistry"/> registrada como
/// singleton, que persiste cada <see cref="ComponentSetVersion"/> como un
/// sidecar JSON en disco (App_Data/components/{ProjectId}/{ImageId}/{VectorId}.json
/// por default, configurable vía <see cref="ComponentRegistryOptions"/>), con
/// escritura atómica vía temp+move -- mismo patrón exacto que
/// <see cref="Vectify.Api.Dimensioning.PersistentDimensionVersionRegistry"/>.
/// Al construirse rehidrata los diccionarios en memoria escaneando esos
/// sidecars (incluido el contador de versión por imagen, tomando el máximo
/// Version visto), así que las lecturas siguen siendo O(1) en memoria pero la
/// fuente de verdad sobrevive a un reinicio del proceso -- cumple el
/// criterio de aceptación de spec.md: "no recalcular en cada request de
/// lectura si ya se calculó para esa combinación layer+versión".
/// </summary>
public sealed class PersistentComponentVersionRegistry : IComponentVersionRegistry
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId), int> _versions = new();
    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId, Guid VectorId), ComponentSetVersion> _byVectorId = new();
    private readonly string _rootPath;
    private readonly ILogger<PersistentComponentVersionRegistry> _logger;

    public PersistentComponentVersionRegistry(
        IOptions<ComponentRegistryOptions> options,
        IHostEnvironment environment,
        ILogger<PersistentComponentVersionRegistry> logger)
    {
        var configuredPath = options.Value.RootPath;
        _rootPath = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(environment.ContentRootPath, configuredPath);
        _logger = logger;

        Directory.CreateDirectory(_rootPath);
        LoadFromDisk();
    }

    public ComponentSetVersion? FindByVectorId(Guid projectId, Guid imageId, Guid vectorId) =>
        _byVectorId.GetValueOrDefault((projectId, imageId, vectorId));

    public int NextVersion(Guid projectId, Guid imageId) =>
        _versions.AddOrUpdate((projectId, imageId), addValueFactory: _ => 1, updateValueFactory: (_, current) => current + 1);

    public void Save(ComponentSetVersion record)
    {
        _byVectorId[(record.ProjectId, record.ImageId, record.VectorId)] = record;
        Persist(record);
    }

    private void Persist(ComponentSetVersion record)
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
                "No se pudo persistir en disco la versión de componentes {ProjectId}/{ImageId}/{VectorId}; sigue disponible en memoria hasta el próximo reinicio del proceso.",
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
                var record = JsonSerializer.Deserialize<ComponentSetVersion>(json, SerializerOptions);
                if (record is null)
                {
                    continue;
                }

                _byVectorId[(record.ProjectId, record.ImageId, record.VectorId)] = record;

                var imageKey = (record.ProjectId, record.ImageId);
                _versions.AddOrUpdate(imageKey, addValueFactory: _ => record.Version, updateValueFactory: (_, current) => Math.Max(current, record.Version));

                loaded++;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "No se pudo leer/parsear el sidecar de componentes '{File}'; se lo ignora.", file);
            }
        }

        _logger.LogInformation(
            "PersistentComponentVersionRegistry rehidratado con {Count} versión(es) desde '{RootPath}'.",
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
