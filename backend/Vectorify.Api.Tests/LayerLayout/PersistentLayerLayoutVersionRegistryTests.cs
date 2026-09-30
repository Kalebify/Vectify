using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Vectorify.Api.LayerLayout;
using Vectorify.Api.Options;

namespace Vectorify.Api.Tests.LayerLayout;

/// <summary>
/// Pruebas de PersistentLayerLayoutVersionRegistry: guardar, encontrar la
/// última versión por paleta+versión confirmada (mismo contrato que
/// InMemoryLayerLayoutVersionRegistry) y que el historial sobrevive a
/// "reiniciar el proceso" -- acá simulado recreando la instancia apuntando al
/// mismo directorio en disco. Mismo criterio que
/// PersistentManufacturingOperationVersionRegistryTests (el precedente más
/// cercano, mismo patrón de sidecar).
/// </summary>
public sealed class PersistentLayerLayoutVersionRegistryTests : IDisposable
{
    private readonly string _rootPath =
        Path.Combine(Path.GetTempPath(), "vectorify-layer-layout-registry-tests-" + Guid.NewGuid().ToString("n"));

    private PersistentLayerLayoutVersionRegistry CreateRegistry()
    {
        var environment = new FakeHostEnvironment { ContentRootPath = _rootPath };
        var options = Microsoft.Extensions.Options.Options.Create(new LayerLayoutRegistryOptions { RootPath = "layer-layout" });
        return new PersistentLayerLayoutVersionRegistry(options, environment, NullLogger<PersistentLayerLayoutVersionRegistry>.Instance);
    }

    private static LayerLayoutSetVersion SampleRecord(
        Guid projectId, Guid imageId, Guid paletteId, int paletteVersion = 1, int version = 1) => new(
        ProjectId: projectId,
        ImageId: imageId,
        PaletteId: paletteId,
        PaletteVersion: paletteVersion,
        LayerSetId: Guid.NewGuid(),
        Version: version,
        Entries: new List<LayerLayoutEntry>
        {
            new(Guid.NewGuid(), Order: 0, Visible: true, Locked: false),
            new(Guid.NewGuid(), Order: 1, Visible: false, Locked: true),
        },
        CreatedAt: DateTimeOffset.UtcNow);

    [Fact]
    public void Save_ThenFindLatest_ReturnsTheSameRecordFromMemory()
    {
        var registry = CreateRegistry();
        var projectId = Guid.NewGuid();
        var imageId = Guid.NewGuid();
        var paletteId = Guid.NewGuid();
        var record = SampleRecord(projectId, imageId, paletteId);

        registry.Save(record);

        var found = registry.FindLatest(projectId, imageId, paletteId, paletteVersion: 1);
        Assert.NotNull(found);
        Assert.Equal(2, found!.Entries.Count);
        Assert.Equal(record.Entries[0].GroupId, found.Entries[0].GroupId);
        Assert.False(found.Entries[1].Visible);
        Assert.True(found.Entries[1].Locked);
    }

    [Fact]
    public void Save_WritesASidecarJsonFileToDisk()
    {
        var registry = CreateRegistry();
        var projectId = Guid.NewGuid();
        var imageId = Guid.NewGuid();
        var paletteId = Guid.NewGuid();
        var record = SampleRecord(projectId, imageId, paletteId, paletteVersion: 3);

        registry.Save(record);

        var expectedPath = Path.Combine(
            _rootPath, "layer-layout", projectId.ToString("N"), imageId.ToString("N"), paletteId.ToString("N"), "3.json");
        Assert.True(File.Exists(expectedPath));
        Assert.False(File.Exists(expectedPath + ".tmp"));
    }

    [Fact]
    public void Save_ThenRecreatingTheRegistryOnTheSameDirectory_StillFindsTheRecord()
    {
        var projectId = Guid.NewGuid();
        var imageId = Guid.NewGuid();
        var paletteId = Guid.NewGuid();
        var record = SampleRecord(projectId, imageId, paletteId);

        var firstRegistry = CreateRegistry();
        firstRegistry.Save(record);

        var secondRegistry = CreateRegistry();

        var found = secondRegistry.FindLatest(projectId, imageId, paletteId, paletteVersion: 1);
        Assert.NotNull(found);
        Assert.Equal(record.Entries.Count, found!.Entries.Count);
    }

    [Fact]
    public void NextVersion_AfterRecreatingTheRegistry_ContinuesFromTheHighestPersistedVersion()
    {
        var projectId = Guid.NewGuid();
        var imageId = Guid.NewGuid();
        var paletteId = Guid.NewGuid();

        var firstRegistry = CreateRegistry();
        firstRegistry.Save(SampleRecord(projectId, imageId, paletteId, paletteVersion: 1, version: 1));
        firstRegistry.Save(SampleRecord(projectId, imageId, paletteId, paletteVersion: 1, version: 2));

        var secondRegistry = CreateRegistry();

        Assert.Equal(3, secondRegistry.NextVersion(projectId, imageId, paletteId, paletteVersion: 1));
    }

    [Fact]
    public void FindLatest_ForADifferentPaletteVersion_ReturnsNull()
    {
        var registry = CreateRegistry();
        var projectId = Guid.NewGuid();
        var imageId = Guid.NewGuid();
        var paletteId = Guid.NewGuid();
        registry.Save(SampleRecord(projectId, imageId, paletteId, paletteVersion: 1));

        Assert.Null(registry.FindLatest(projectId, imageId, paletteId, paletteVersion: 2));
    }

    [Fact]
    public void FindLatest_WhenRecordWasNeverSaved_ReturnsNull()
    {
        var registry = CreateRegistry();

        Assert.Null(registry.FindLatest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), paletteVersion: 1));
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
        public string ApplicationName { get; set; } = "Vectorify.Api.Tests";
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
