using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Vectify.Api.Options;
using Vectify.Api.Threshold;

namespace Vectify.Api.Tests.Threshold;

/// <summary>
/// Pruebas de PersistentThresholdConfigRegistry: guardar, encontrar por
/// params/última versión/maskId (mismo contrato que InMemoryThresholdConfigRegistry) y,
/// sobre todo, que el historial sobrevive a "reiniciar el proceso" -- acá simulado
/// recreando la instancia apuntando al mismo directorio en disco. Mismo criterio que
/// PersistentProjectRegistryTests (Defecto 2 de la ronda de QA sobre M1-S05/M1-S06).
/// </summary>
public sealed class PersistentThresholdConfigRegistryTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "vectify-threshold-registry-tests-" + Guid.NewGuid().ToString("n"));

    private PersistentThresholdConfigRegistry CreateRegistry()
    {
        var environment = new FakeHostEnvironment { ContentRootPath = _rootPath };
        var options = Microsoft.Extensions.Options.Options.Create(new ThresholdRegistryOptions { RootPath = "thresholds" });
        return new PersistentThresholdConfigRegistry(options, environment, NullLogger<PersistentThresholdConfigRegistry>.Instance);
    }

    private static ThresholdConfigRecord SampleRecord(
        Guid projectId, Guid imageId, Guid sourcePreviewId, int version = 1, Guid? maskId = null) => new(
        ProjectId: projectId,
        ImageId: imageId,
        Version: version,
        MaskId: maskId ?? Guid.NewGuid(),
        SourcePreviewId: sourcePreviewId,
        Parameters: new ThresholdParameters(128, false),
        MaskStorageKey: "project/image/masks/mask.png",
        ContentType: "image/png",
        Width: 10,
        Height: 10,
        Metrics: new ThresholdMetrics(50.0, 50.0, false, false, null, null),
        CreatedAt: DateTimeOffset.UtcNow);

    [Fact]
    public void Save_ThenFindByParams_ReturnsTheSameRecordFromMemory()
    {
        var registry = CreateRegistry();
        var projectId = Guid.NewGuid();
        var imageId = Guid.NewGuid();
        var sourcePreviewId = Guid.NewGuid();
        var record = SampleRecord(projectId, imageId, sourcePreviewId);

        registry.Save(record);

        var found = registry.FindByParams(projectId, imageId, sourcePreviewId, record.Parameters);
        Assert.NotNull(found);
        Assert.Equal(record.MaskId, found!.MaskId);
    }

    [Fact]
    public void Save_WritesASidecarJsonFileToDisk()
    {
        var registry = CreateRegistry();
        var projectId = Guid.NewGuid();
        var imageId = Guid.NewGuid();
        var record = SampleRecord(projectId, imageId, Guid.NewGuid());

        registry.Save(record);

        var expectedPath = Path.Combine(
            _rootPath, "thresholds", projectId.ToString("N"), imageId.ToString("N"), $"{record.MaskId:N}.json");
        Assert.True(File.Exists(expectedPath));
        Assert.False(File.Exists(expectedPath + ".tmp"));
    }

    [Fact]
    public void Save_ThenRecreatingTheRegistryOnTheSameDirectory_StillFindsTheRecordByAllLookups()
    {
        var projectId = Guid.NewGuid();
        var imageId = Guid.NewGuid();
        var sourcePreviewId = Guid.NewGuid();
        var record = SampleRecord(projectId, imageId, sourcePreviewId);

        var firstRegistry = CreateRegistry();
        firstRegistry.Save(record);

        // Simula un reinicio del proceso: nueva instancia, mismo directorio en disco.
        var secondRegistry = CreateRegistry();

        var byParams = secondRegistry.FindByParams(projectId, imageId, sourcePreviewId, record.Parameters);
        Assert.NotNull(byParams);
        Assert.Equal(record.MaskId, byParams!.MaskId);

        var latest = secondRegistry.FindLatest(projectId, imageId);
        Assert.NotNull(latest);
        Assert.Equal(record.MaskId, latest!.MaskId);

        var byMaskId = secondRegistry.FindByMaskId(projectId, imageId, record.MaskId);
        Assert.NotNull(byMaskId);
        Assert.Equal(record.MaskStorageKey, byMaskId!.MaskStorageKey);
    }

    [Fact]
    public void NextVersion_AfterRecreatingTheRegistry_ContinuesFromTheHighestPersistedVersion()
    {
        var projectId = Guid.NewGuid();
        var imageId = Guid.NewGuid();
        var sourcePreviewId = Guid.NewGuid();

        var firstRegistry = CreateRegistry();
        firstRegistry.Save(SampleRecord(projectId, imageId, sourcePreviewId, version: 1));
        firstRegistry.Save(SampleRecord(projectId, imageId, sourcePreviewId, version: 2));

        // Simula un reinicio del proceso: nueva instancia, mismo directorio en disco.
        var secondRegistry = CreateRegistry();

        Assert.Equal(3, secondRegistry.NextVersion(projectId, imageId));
    }

    [Fact]
    public void FindByParams_WhenRecordWasNeverSaved_ReturnsNull()
    {
        var registry = CreateRegistry();

        Assert.Null(registry.FindByParams(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new ThresholdParameters(128, false)));
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
