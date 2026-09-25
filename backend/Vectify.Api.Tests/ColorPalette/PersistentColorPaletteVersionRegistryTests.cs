using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Vectify.Api.ColorPalette;
using Vectify.Api.Options;

namespace Vectify.Api.Tests.ColorPalette;

/// <summary>
/// Pruebas de PersistentColorPaletteVersionRegistry: guardar, encontrar por
/// params/última versión (mismo contrato que InMemoryColorPaletteVersionRegistry)
/// y que el historial sobrevive a "reiniciar el proceso" -- simulado
/// recreando la instancia apuntando al mismo directorio en disco. Mismo
/// criterio que PersistentSimplificationVersionRegistryTests.
/// </summary>
public sealed class PersistentColorPaletteVersionRegistryTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "vectify-color-palette-registry-tests-" + Guid.NewGuid().ToString("n"));

    private PersistentColorPaletteVersionRegistry CreateRegistry()
    {
        var environment = new FakeHostEnvironment { ContentRootPath = _rootPath };
        var options = Microsoft.Extensions.Options.Options.Create(new ColorPaletteRegistryOptions { RootPath = "color-palettes" });
        return new PersistentColorPaletteVersionRegistry(options, environment, NullLogger<PersistentColorPaletteVersionRegistry>.Instance);
    }

    private static ColorGroup SampleGroup(string name = "Color 1") => new(
        GroupId: Guid.NewGuid(),
        Name: name,
        ColorHex: "#a1b2c3",
        PixelCount: 100,
        AreaPercent: 50.0,
        HasPartialAlpha: false,
        RawGroupIds: new[] { 0 },
        MaskStorageKey: "project/image/color-palette/palette/masks/group.png",
        MergedFrom: null);

    private static ColorPaletteVersion SampleRecord(
        Guid projectId, Guid imageId, Guid paletteId, int version = 1) => new(
        ProjectId: projectId,
        ImageId: imageId,
        Version: version,
        PaletteId: paletteId,
        DetectionParameters: new ColorPaletteParameters(12.0, 8),
        Groups: new[] { SampleGroup() },
        TransparentPercent: 0.0,
        SourceWidthPx: 100,
        SourceHeightPx: 80,
        QuantizedPreviewStorageKey: "project/image/color-palette/palette/preview/1.png",
        IsConfirmed: false,
        CreatedAt: DateTimeOffset.UtcNow);

    [Fact]
    public void Save_ThenFindByParams_ReturnsTheSameRecordFromMemory()
    {
        var registry = CreateRegistry();
        var projectId = Guid.NewGuid();
        var imageId = Guid.NewGuid();
        var paletteId = Guid.NewGuid();
        var record = SampleRecord(projectId, imageId, paletteId);

        registry.Save(record);

        var found = registry.FindByParams(projectId, imageId, paletteId, record.DetectionParameters);
        Assert.NotNull(found);
        Assert.Equal(record.PaletteId, found!.PaletteId);
        Assert.Single(found.Groups);
    }

    [Fact]
    public void Save_WritesASidecarJsonFileToDisk()
    {
        var registry = CreateRegistry();
        var projectId = Guid.NewGuid();
        var imageId = Guid.NewGuid();
        var paletteId = Guid.NewGuid();
        var record = SampleRecord(projectId, imageId, paletteId);

        registry.Save(record);

        var expectedPath = Path.Combine(
            _rootPath, "color-palettes", projectId.ToString("N"), imageId.ToString("N"), paletteId.ToString("N"), "1.json");
        Assert.True(File.Exists(expectedPath));
        Assert.False(File.Exists(expectedPath + ".tmp"));
    }

    [Fact]
    public void Save_ThenRecreatingTheRegistryOnTheSameDirectory_StillFindsTheRecordByAllLookups()
    {
        var projectId = Guid.NewGuid();
        var imageId = Guid.NewGuid();
        var paletteId = Guid.NewGuid();
        var record = SampleRecord(projectId, imageId, paletteId);

        var firstRegistry = CreateRegistry();
        firstRegistry.Save(record);

        // Simula un reinicio del proceso: nueva instancia, mismo directorio en disco.
        var secondRegistry = CreateRegistry();

        var byParams = secondRegistry.FindByParams(projectId, imageId, paletteId, record.DetectionParameters);
        Assert.NotNull(byParams);
        Assert.Equal(record.PaletteId, byParams!.PaletteId);

        var latest = secondRegistry.FindLatest(projectId, imageId, paletteId);
        Assert.NotNull(latest);
        Assert.Equal(record.QuantizedPreviewStorageKey, latest!.QuantizedPreviewStorageKey);
    }

    [Fact]
    public void NextVersion_AfterRecreatingTheRegistry_ContinuesFromTheHighestPersistedVersion()
    {
        var projectId = Guid.NewGuid();
        var imageId = Guid.NewGuid();
        var paletteId = Guid.NewGuid();

        var firstRegistry = CreateRegistry();
        firstRegistry.Save(SampleRecord(projectId, imageId, paletteId, version: 1));
        firstRegistry.Save(SampleRecord(projectId, imageId, paletteId, version: 2));

        // Simula un reinicio del proceso: nueva instancia, mismo directorio en disco.
        var secondRegistry = CreateRegistry();

        Assert.Equal(3, secondRegistry.NextVersion(projectId, imageId, paletteId));
    }

    [Fact]
    public void FindByParams_WhenRecordWasNeverSaved_ReturnsNull()
    {
        var registry = CreateRegistry();

        Assert.Null(registry.FindByParams(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new ColorPaletteParameters(12.0, null)));
    }

    [Fact]
    public void FindLatest_DifferentPaletteIdsForSameImage_AreIndependentSessions()
    {
        var registry = CreateRegistry();
        var projectId = Guid.NewGuid();
        var imageId = Guid.NewGuid();
        var firstPaletteId = Guid.NewGuid();
        var secondPaletteId = Guid.NewGuid();

        registry.Save(SampleRecord(projectId, imageId, firstPaletteId));
        registry.Save(SampleRecord(projectId, imageId, secondPaletteId));

        Assert.Equal(1, registry.FindLatest(projectId, imageId, firstPaletteId)!.Version);
        Assert.Equal(1, registry.FindLatest(projectId, imageId, secondPaletteId)!.Version);

        // Los contadores de versión son independientes por sesión (PaletteId):
        // avanzar el de una no afecta el de la otra.
        Assert.Equal(1, registry.NextVersion(projectId, imageId, firstPaletteId));
        Assert.Equal(1, registry.NextVersion(projectId, imageId, secondPaletteId));
        Assert.Equal(2, registry.NextVersion(projectId, imageId, firstPaletteId));
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Vectify.Api.Tests";
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
