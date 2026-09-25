using System.Collections.Concurrent;

namespace Vectify.Api.ColorPalette;

/// <summary>
/// Implementación en memoria de <see cref="IColorPaletteVersionRegistry"/>,
/// registrada como singleton. Mismo criterio que InMemorySimplificationVersionRegistry.
/// </summary>
public sealed class InMemoryColorPaletteVersionRegistry : IColorPaletteVersionRegistry
{
    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId, Guid PaletteId), int> _versions = new();
    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId, Guid PaletteId, string ParamsKey), ColorPaletteVersion> _byParams = new();
    private readonly ConcurrentDictionary<(Guid ProjectId, Guid ImageId, Guid PaletteId), ColorPaletteVersion> _latest = new();

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
    }
}
