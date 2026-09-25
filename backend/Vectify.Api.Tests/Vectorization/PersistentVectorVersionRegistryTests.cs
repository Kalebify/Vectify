using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Vectify.Api.Options;
using Vectify.Api.Vectorization;

namespace Vectify.Api.Tests.Vectorization;

/// <summary>
/// Pruebas de PersistentVectorVersionRegistry: guardar, encontrar por
/// params/última versión/vectorId (mismo contrato que InMemoryVectorVersionRegistry) y,
/// sobre todo, que el historial sobrevive a "reiniciar el proceso" -- acá simulado
/// recreando la instancia apuntando al mismo directorio en disco. Mismo criterio que
/// PersistentProjectRegistryTests/PersistentThresholdConfigRegistryTests (Defecto 2 de
/// la ronda de QA sobre M1-S05/M1-S06).
/// </summary>
public sealed class PersistentVectorVersionRegistryTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "vectify-vector-registry-tests-" + Guid.NewGuid().ToString("n"));

    private PersistentVectorVersionRegistry CreateRegistry()
    {
        var environment = new FakeHostEnvironment { ContentRootPath = _rootPath };
        var options = Microsoft.Extensions.Options.Options.Create(new VectorRegistryOptions { RootPath = "vectors" });
        return new PersistentVectorVersionRegistry(options, environment, NullLogger<PersistentVectorVersionRegistry>.Instance);
    }

    private static VectorVersion SampleRecord(
        Guid projectId, Guid imageId, Guid sourceMaskId, int version = 1, Guid? vectorId = null) => new(
        ProjectId: projectId,
        ImageId: imageId,
        Version: version,
        VectorId: vectorId ?? Guid.NewGuid(),
        SourceMaskId: sourceMaskId,
        Parameters: new VectorParameters(),
        SvgStorageKey: "project/image/vectors/result.svg",
        ContentType: "image/svg+xml",
        Width: 10,
        Height: 10,
        Metrics: new VectorMetrics(1, 4, new VectorBounds(0, 0, 10, 10, 10, 10)),
        CreatedAt: DateTimeOffset.UtcNow);

    [Fact]
    public void Save_ThenFindByParams_ReturnsTheSameRecordFromMemory()
    {
        var registry = CreateRegistry();
        var projectId = Guid.NewGuid();
        var imageId = Guid.NewGuid();
        var sourceMaskId = Guid.NewGuid();
        var record = SampleRecord(projectId, imageId, sourceMaskId);

        registry.Save(record);

        var found = registry.FindByParams(projectId, imageId, sourceMaskId, record.Parameters);
        Assert.NotNull(found);
        Assert.Equal(record.VectorId, found!.VectorId);
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
            _rootPath, "vectors", projectId.ToString("N"), imageId.ToString("N"), $"{record.VectorId:N}.json");
        Assert.True(File.Exists(expectedPath));
        Assert.False(File.Exists(expectedPath + ".tmp"));
    }

    [Fact]
    public void Save_ThenRecreatingTheRegistryOnTheSameDirectory_StillFindsTheRecordByAllLookups()
    {
        var projectId = Guid.NewGuid();
        var imageId = Guid.NewGuid();
        var sourceMaskId = Guid.NewGuid();
        var record = SampleRecord(projectId, imageId, sourceMaskId);

        var firstRegistry = CreateRegistry();
        firstRegistry.Save(record);

        // Simula un reinicio del proceso: nueva instancia, mismo directorio en disco.
        var secondRegistry = CreateRegistry();

        var byParams = secondRegistry.FindByParams(projectId, imageId, sourceMaskId, record.Parameters);
        Assert.NotNull(byParams);
        Assert.Equal(record.VectorId, byParams!.VectorId);

        var latest = secondRegistry.FindLatest(projectId, imageId);
        Assert.NotNull(latest);
        Assert.Equal(record.VectorId, latest!.VectorId);

        var byVectorId = secondRegistry.FindByVectorId(projectId, imageId, record.VectorId);
        Assert.NotNull(byVectorId);
        Assert.Equal(record.SvgStorageKey, byVectorId!.SvgStorageKey);
    }

    [Fact]
    public void NextVersion_AfterRecreatingTheRegistry_ContinuesFromTheHighestPersistedVersion()
    {
        var projectId = Guid.NewGuid();
        var imageId = Guid.NewGuid();
        var sourceMaskId = Guid.NewGuid();

        var firstRegistry = CreateRegistry();
        firstRegistry.Save(SampleRecord(projectId, imageId, sourceMaskId, version: 1));
        firstRegistry.Save(SampleRecord(projectId, imageId, sourceMaskId, version: 2));

        // Simula un reinicio del proceso: nueva instancia, mismo directorio en disco.
        var secondRegistry = CreateRegistry();

        Assert.Equal(3, secondRegistry.NextVersion(projectId, imageId));
    }

    [Fact]
    public void FindByParams_WhenRecordWasNeverSaved_ReturnsNull()
    {
        var registry = CreateRegistry();

        Assert.Null(registry.FindByParams(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new VectorParameters()));
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
