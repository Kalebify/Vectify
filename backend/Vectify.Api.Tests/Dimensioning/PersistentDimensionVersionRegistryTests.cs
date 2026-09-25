using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Vectify.Api.Dimensioning;
using Vectify.Api.Options;

namespace Vectify.Api.Tests.Dimensioning;

/// <summary>
/// Pruebas de PersistentDimensionVersionRegistry: guardar, encontrar por
/// params/dimensionId (mismo contrato que InMemoryDimensionVersionRegistry)
/// y, sobre todo, que el historial sobrevive a "reiniciar el proceso" -- acá
/// simulado recreando la instancia apuntando al mismo directorio en disco.
/// Mismo criterio que PersistentSimplificationVersionRegistryTests.
/// </summary>
public sealed class PersistentDimensionVersionRegistryTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "vectify-dimension-registry-tests-" + Guid.NewGuid().ToString("n"));

    private PersistentDimensionVersionRegistry CreateRegistry()
    {
        var environment = new FakeHostEnvironment { ContentRootPath = _rootPath };
        var options = Microsoft.Extensions.Options.Options.Create(new DimensionRegistryOptions { RootPath = "dimensions" });
        return new PersistentDimensionVersionRegistry(options, environment, NullLogger<PersistentDimensionVersionRegistry>.Instance);
    }

    private static DimensionVersion SampleRecord(
        Guid projectId, Guid imageId, Guid sourceId, int version = 1, Guid? dimensionId = null) => new(
        ProjectId: projectId,
        ImageId: imageId,
        Version: version,
        DimensionId: dimensionId ?? Guid.NewGuid(),
        SourceId: sourceId,
        SourceKind: DimensionSourceKind.Vector,
        Parameters: new DimensionParameters(100, 100, true),
        SvgStorageKey: "project/image/dimensions/result.svg",
        ContentType: "image/svg+xml",
        SourceWidthPx: 10,
        SourceHeightPx: 10,
        CreatedAt: DateTimeOffset.UtcNow);

    [Fact]
    public void Save_ThenFindByParams_ReturnsTheSameRecordFromMemory()
    {
        var registry = CreateRegistry();
        var projectId = Guid.NewGuid();
        var imageId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var record = SampleRecord(projectId, imageId, sourceId);

        registry.Save(record);

        var found = registry.FindByParams(projectId, imageId, sourceId, DimensionSourceKind.Vector, record.Parameters);
        Assert.NotNull(found);
        Assert.Equal(record.DimensionId, found!.DimensionId);
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
            _rootPath, "dimensions", projectId.ToString("N"), imageId.ToString("N"), $"{record.DimensionId:N}.json");
        Assert.True(File.Exists(expectedPath));
        Assert.False(File.Exists(expectedPath + ".tmp"));
    }

    [Fact]
    public void Save_ThenRecreatingTheRegistryOnTheSameDirectory_StillFindsTheRecordByAllLookups()
    {
        var projectId = Guid.NewGuid();
        var imageId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var record = SampleRecord(projectId, imageId, sourceId);

        var firstRegistry = CreateRegistry();
        firstRegistry.Save(record);

        var secondRegistry = CreateRegistry();

        var byParams = secondRegistry.FindByParams(projectId, imageId, sourceId, DimensionSourceKind.Vector, record.Parameters);
        Assert.NotNull(byParams);
        Assert.Equal(record.DimensionId, byParams!.DimensionId);

        var byDimensionId = secondRegistry.FindByDimensionId(projectId, imageId, record.DimensionId);
        Assert.NotNull(byDimensionId);
        Assert.Equal(record.SvgStorageKey, byDimensionId!.SvgStorageKey);
    }

    [Fact]
    public void NextVersion_AfterRecreatingTheRegistry_ContinuesFromTheHighestPersistedVersion()
    {
        var projectId = Guid.NewGuid();
        var imageId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();

        var firstRegistry = CreateRegistry();
        firstRegistry.Save(SampleRecord(projectId, imageId, sourceId, version: 1));
        firstRegistry.Save(SampleRecord(projectId, imageId, sourceId, version: 2));

        var secondRegistry = CreateRegistry();

        Assert.Equal(3, secondRegistry.NextVersion(projectId, imageId));
    }

    [Fact]
    public void FindByParams_WhenRecordWasNeverSaved_ReturnsNull()
    {
        var registry = CreateRegistry();

        Assert.Null(registry.FindByParams(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DimensionSourceKind.Vector, new DimensionParameters(100, 100, true)));
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
