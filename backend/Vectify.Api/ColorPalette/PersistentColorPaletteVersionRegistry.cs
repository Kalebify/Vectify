using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Vectify.Api.Options;

namespace Vectify.Api.ColorPalette;

/// <summary>
/// Implementación de <see cref="IColorPaletteVersionRegistry"/> registrada
/// como singleton, que persiste cada <see cref="ColorPaletteVersion"/> como
/// un sidecar JSON en disco (App_Data/color-palettes/{ProjectId}/{ImageId}/{PaletteId}/{Version}.json
/// por default, configurable vía <see cref="ColorPaletteRegistryOptions"/>),
/// con escritura atómica vía temp+move -- mismo patrón exacto que
/// PersistentSimplificationVersionRegistry/PersistentThresholdConfigRegistry.
/// Al construirse rehidrata los diccionarios en memoria escaneando esos
/// sidecars (incluido el contador de versión por sesión, tomando el máximo
/// Version visto), así que las lecturas siguen siendo O(1) en memoria pero
/// la fuente de verdad sobrevive a un reinicio del proceso.
/// </summary>
public sealed class PersistentColorPaletteVersionRegistry : IColorPaletteVersionRegistry
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId, Guid PaletteId), int> _versions = new();
    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId, Guid PaletteId, string ParamsKey), ColorPaletteVersion> _byParams = new();
    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId, Guid PaletteId), ColorPaletteVersion> _latest = new();
    private readonly string _rootPath;
    private readonly ILogger<PersistentColorPaletteVersionRegistry> _logger;

    public PersistentColorPaletteVersionRegistry(
        IOptions<ColorPaletteRegistryOptions> options,
        IHostEnvironment environment,
        ILogger<PersistentColorPaletteVersionRegistry> logger)
    {
        var configuredPath = options.Value.RootPath;
        _rootPath = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(environment.ContentRootPath, configuredPath);
        _logger = logger;

        Directory.CreateDirectory(_rootPath);
        LoadFromDisk();
    }

    public ColorPaletteVersion? FindByParams(Guid projectId, Guid imageId, Guid paletteId, ColorPaletteParameters parameters) =>
        _byParams.GetValueOrDefault((projectId, imageId, paletteId, parameters.ToCacheKey()));

    public ColorPaletteVersion? FindLatest(Guid projectId, Guid imageId, Guid paletteId) =>
        _latest.GetValueOrDefault((projectId, imageId, paletteId));

    public int NextVersion(Guid projectId, Guid imageId, Guid paletteId) =>
        _versions.AddOrUpdate((projectId, imageId, paletteId), addValueFactory: _ => 1, updateValueFactory: (_, current) => current + 1);

    public void Save(ColorPaletteVersion record)
    {
        var sessionKey = (record.ProjectId, record.ImageId, record.PaletteId);
        _byParams[(record.ProjectId, record.ImageId, record.PaletteId, record.DetectionParameters.ToCacheKey())] = record;
        _latest[sessionKey] = record;

        Persist(record);
    }

    private void Persist(ColorPaletteVersion record)
    {
        var sessionDir = Path.Combine(
            _rootPath, record.ProjectId.ToString("N"), record.ImageId.ToString("N"), record.PaletteId.ToString("N"));
        var finalPath = Path.Combine(sessionDir, $"{record.Version}.json");
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
                "No se pudo persistir en disco la versión de paleta de colores {ProjectId}/{ImageId}/{PaletteId} v{Version}; sigue disponible en memoria hasta el próximo reinicio del proceso.",
                record.ProjectId,
                record.ImageId,
                record.PaletteId,
                record.Version);
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
                var record = JsonSerializer.Deserialize<ColorPaletteVersion>(json, SerializerOptions);
                if (record is null)
                {
                    continue;
                }

                var sessionKey = (record.ProjectId, record.ImageId, record.PaletteId);
                _byParams[(record.ProjectId, record.ImageId, record.PaletteId, record.DetectionParameters.ToCacheKey())] = record;

                if (!_latest.TryGetValue(sessionKey, out var currentLatest) || record.Version > currentLatest.Version)
                {
                    _latest[sessionKey] = record;
                }

                _versions.AddOrUpdate(sessionKey, addValueFactory: _ => record.Version, updateValueFactory: (_, current) => Math.Max(current, record.Version));

                loaded++;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "No se pudo leer/parsear el sidecar de paleta de colores '{File}'; se lo ignora.", file);
            }
        }

        _logger.LogInformation(
            "PersistentColorPaletteVersionRegistry rehidratado con {Count} versión(es) desde '{RootPath}'.",
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
