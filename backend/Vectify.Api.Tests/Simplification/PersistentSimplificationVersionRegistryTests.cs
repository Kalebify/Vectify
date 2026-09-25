using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Vectify.Api.Options;
using Vectify.Api.Simplification;
using Vectify.Api.Vectorization;

namespace Vectify.Api.Tests.Simplification;

/// <summary>
/// Pruebas de PersistentSimplificationVersionRegistry: guardar, encontrar por
/// params/última versión/simplificationId (mismo contrato que
/// InMemorySimplificationVersionRegistry) y, sobre todo, que el historial
/// sobrevive a "reiniciar el proceso" -- acá simulado recreando la instancia
/// apuntando al mismo directorio en disco. Mismo criterio que
/// PersistentVectorVersionRegistryTests (M1-S05/M1-S06).
/// </summary>
public sealed class PersistentSimplificationVersionRegistryTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "vectify-simplification-registry-tests-" + Guid.NewGuid().ToString("n"));

    private PersistentSimplificationVersionRegistry CreateRegistry()
    {
        var environment = new FakeHostEnvironment { ContentRootPath = _rootPath };
        var options = Microsoft.Extensions.Options.Options.Create(new SimplificationRegistryOptions { RootPath = "simplifications" });
        return new PersistentSimplificationVersionRegistry(options, environment, NullLogger<PersistentSimplificationVersionRegistry>.Instance);
    }

    private static SimplificationVersion SampleRecord(
        Guid projectId, Guid imageId, Guid sourceVectorId, int version = 1, Guid? simplificationId = null) => new(
        ProjectId: projectId,
        ImageId: imageId,
        Version: version,
        SimplificationId: simplificationId ?? Guid.NewGuid(),
        SourceVectorId: sourceVectorId,
        Parameters: new SimplificationParameters(0.004, "medium"),
        SvgStorageKey: "project/image/simplifications/result.svg",
        ContentType: "image/svg+xml",
        Width: 10,
        Height: 10,
        Metrics: new SimplificationMetrics(
            new VectorMetrics(1, 12, new VectorBounds(0, 0, 10, 10, 10, 10)),
            new VectorMetrics(1, 4, new VectorBounds(0, 0, 10, 10, 10, 10)),
            66.7),
        CreatedAt: DateTimeOffset.UtcNow);

    [Fact]
    public void Save_ThenFindByParams_ReturnsTheSameRecordFromMemory()
    {
        var registry = CreateRegistry();
        var projectId = Guid.NewGuid();
        var imageId = Guid.NewGuid();
        var sourceVectorId = Guid.NewGuid();
        var record = SampleRecord(projectId, imageId, sourceVectorId);

        registry.Save(record);

        var found = registry.FindByParams(projectId, imageId, sourceVectorId, record.Parameters);
        Assert.NotNull(found);
        Assert.Equal(record.SimplificationId, found!.SimplificationId);
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
            _rootPath, "simplifications", projectId.ToString("N"), imageId.ToString("N"), $"{record.SimplificationId:N}.json");
        Assert.True(File.Exists(expectedPath));
        Assert.False(File.Exists(expectedPath + ".tmp"));
    }

    [Fact]
    public void Save_ThenRecreatingTheRegistryOnTheSameDirectory_StillFindsTheRecordByAllLookups()
    {
        var projectId = Guid.NewGuid();
        var imageId = Guid.NewGuid();
        var sourceVectorId = Guid.NewGuid();
        var record = SampleRecord(projectId, imageId, sourceVectorId);

        var firstRegistry = CreateRegistry();
        firstRegistry.Save(record);

        // Simula un reinicio del proceso: nueva instancia, mismo directorio en disco.
        var secondRegistry = CreateRegistry();

        var byParams = secondRegistry.FindByParams(projectId, imageId, sourceVectorId, record.Parameters);
        Assert.NotNull(byParams);
        Assert.Equal(record.SimplificationId, byParams!.SimplificationId);

        var latest = secondRegistry.FindLatest(projectId, imageId);
        Assert.NotNull(latest);
        Assert.Equal(record.SimplificationId, latest!.SimplificationId);

        var bySimplificationId = secondRegistry.FindBySimplificationId(projectId, imageId, record.SimplificationId);
        Assert.NotNull(bySimplificationId);
        Assert.Equal(record.SvgStorageKey, bySimplificationId!.SvgStorageKey);
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

        // Simula un reinicio del proceso: nueva instancia, mismo directorio en disco.
        var secondRegistry = CreateRegistry();

        Assert.Equal(3, secondRegistry.NextVersion(projectId, imageId));
    }

    [Fact]
    public void FindByParams_WhenRecordWasNeverSaved_ReturnsNull()
    {
        var registry = CreateRegistry();

        Assert.Null(registry.FindByParams(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new SimplificationParameters(0.004, "medium")));
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
