using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Vectify.Api.Options;
using Vectify.Api.PhysicalUnion;

namespace Vectify.Api.Tests.PhysicalUnion;

/// <summary>
/// Pruebas de PersistentPhysicalUnionVersionRegistry: guardar, encontrar la
/// última versión por VectorId de origen, y que el historial sobrevive a
/// "reiniciar el proceso" -- mismo criterio que
/// PersistentComponentGroupVersionRegistryTests.
/// </summary>
public sealed class PersistentPhysicalUnionVersionRegistryTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "vectify-physical-union-registry-tests-" + Guid.NewGuid().ToString("n"));

    private PersistentPhysicalUnionVersionRegistry CreateRegistry()
    {
        var environment = new FakeHostEnvironment { ContentRootPath = _rootPath };
        var options = Microsoft.Extensions.Options.Options.Create(new PhysicalUnionRegistryOptions { RootPath = "physical-unions" });
        return new PersistentPhysicalUnionVersionRegistry(options, environment, NullLogger<PersistentPhysicalUnionVersionRegistry>.Instance);
    }

    private static Vectify.Api.PhysicalUnion.PhysicalUnionVersion SampleRecord(
        Guid projectId, Guid imageId, Guid sourceVectorId, int version = 1) => new(
        ProjectId: projectId,
        ImageId: imageId,
        Version: version,
        SourceVectorId: sourceVectorId,
        ComponentIds: new List<string> { "component-1", "component-2" },
        ResultVectorId: Guid.NewGuid(),
        ComponentCountBefore: 2,
        ResultComponentCount: 1,
        Strategy: "bridge",
        BridgeCount: 1,
        CreatedAt: DateTimeOffset.UtcNow);

    [Fact]
    public void Save_ThenFindLatest_ReturnsTheSameRecordFromMemory()
    {
        var registry = CreateRegistry();
        var projectId = Guid.NewGuid();
        var imageId = Guid.NewGuid();
        var sourceVectorId = Guid.NewGuid();
        var record = SampleRecord(projectId, imageId, sourceVectorId);

        registry.Save(record);

        var found = registry.FindLatest(projectId, imageId, sourceVectorId);
        Assert.NotNull(found);
        Assert.Equal(record.ResultVectorId, found!.ResultVectorId);
    }

    [Fact]
    public void Save_WritesASidecarJsonFileToDisk()
    {
        var registry = CreateRegistry();
        var projectId = Guid.NewGuid();
        var imageId = Guid.NewGuid();
        var sourceVectorId = Guid.NewGuid();
        var record = SampleRecord(projectId, imageId, sourceVectorId);

        registry.Save(record);

        var expectedPath = Path.Combine(
            _rootPath, "physical-unions", projectId.ToString("N"), imageId.ToString("N"), $"{sourceVectorId:N}.json");
        Assert.True(File.Exists(expectedPath));
        Assert.False(File.Exists(expectedPath + ".tmp"));
    }

    [Fact]
    public void Save_ThenRecreatingTheRegistryOnTheSameDirectory_StillFindsTheRecord()
    {
        var projectId = Guid.NewGuid();
        var imageId = Guid.NewGuid();
        var sourceVectorId = Guid.NewGuid();
        var record = SampleRecord(projectId, imageId, sourceVectorId);

        var firstRegistry = CreateRegistry();
        firstRegistry.Save(record);

        var secondRegistry = CreateRegistry();

        var found = secondRegistry.FindLatest(projectId, imageId, sourceVectorId);
        Assert.NotNull(found);
        Assert.Equal(record.ResultVectorId, found!.ResultVectorId);
    }

    [Fact]
    public void NextVersion_AfterRecreatingTheRegistry_ContinuesFromTheHighestPersistedVersion()
    {
        var projectId = Guid.NewGuid();
        var imageId = Guid.NewGuid();
        var sourceVectorId = Guid.NewGuid();

        var firstRegistry = CreateRegistry();
        firstRegistry.Save(SampleRecord(projectId, imageId, sourceVectorId, version: 1));
        firstRegistry.Save(SampleRecord(projectId, imageId, sourceVectorId, version: 2));

        var secondRegistry = CreateRegistry();

        Assert.Equal(3, secondRegistry.NextVersion(projectId, imageId, sourceVectorId));
    }

    [Fact]
    public void FindLatest_WhenRecordWasNeverSaved_ReturnsNull()
    {
        var registry = CreateRegistry();

        Assert.Null(registry.FindLatest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));
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
