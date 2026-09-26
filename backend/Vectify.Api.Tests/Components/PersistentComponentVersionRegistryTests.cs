using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Vectify.Api.Components;
using Vectify.Api.Options;

namespace Vectify.Api.Tests.Components;

/// <summary>
/// Pruebas de PersistentComponentVersionRegistry: guardar, encontrar por
/// VectorId (mismo contrato que InMemoryComponentVersionRegistry) y, sobre
/// todo, que el historial sobrevive a "reiniciar el proceso" -- acá simulado
/// recreando la instancia apuntando al mismo directorio en disco. Mismo
/// criterio que PersistentDimensionVersionRegistryTests.
/// </summary>
public sealed class PersistentComponentVersionRegistryTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "vectify-component-registry-tests-" + Guid.NewGuid().ToString("n"));

    private PersistentComponentVersionRegistry CreateRegistry()
    {
        var environment = new FakeHostEnvironment { ContentRootPath = _rootPath };
        var options = Microsoft.Extensions.Options.Options.Create(new ComponentRegistryOptions { RootPath = "components" });
        return new PersistentComponentVersionRegistry(options, environment, NullLogger<PersistentComponentVersionRegistry>.Instance);
    }

    private static ComponentSetVersion SampleRecord(
        Guid projectId, Guid imageId, Guid vectorId, int version = 1, Guid? componentSetId = null) => new(
        ProjectId: projectId,
        ImageId: imageId,
        Version: version,
        ComponentSetId: componentSetId ?? Guid.NewGuid(),
        VectorId: vectorId,
        Components: new List<LayerComponent>
        {
            new(
                "component-1",
                new List<ComponentMember> { new(0, 0, "solid", new ComponentBounds(2, 2, 8, 8), 36) },
                new ComponentBounds(2, 2, 8, 8),
                36,
                false),
        },
        SkippedPathCount: 0,
        CreatedAt: DateTimeOffset.UtcNow);

    [Fact]
    public void Save_ThenFindByVectorId_ReturnsTheSameRecordFromMemory()
    {
        var registry = CreateRegistry();
        var projectId = Guid.NewGuid();
        var imageId = Guid.NewGuid();
        var vectorId = Guid.NewGuid();
        var record = SampleRecord(projectId, imageId, vectorId);

        registry.Save(record);

        var found = registry.FindByVectorId(projectId, imageId, vectorId);
        Assert.NotNull(found);
        Assert.Equal(record.ComponentSetId, found!.ComponentSetId);
    }

    [Fact]
    public void Save_WritesASidecarJsonFileToDisk()
    {
        var registry = CreateRegistry();
        var projectId = Guid.NewGuid();
        var imageId = Guid.NewGuid();
        var vectorId = Guid.NewGuid();
        var record = SampleRecord(projectId, imageId, vectorId);

        registry.Save(record);

        var expectedPath = Path.Combine(
            _rootPath, "components", projectId.ToString("N"), imageId.ToString("N"), $"{vectorId:N}.json");
        Assert.True(File.Exists(expectedPath));
        Assert.False(File.Exists(expectedPath + ".tmp"));
    }

    [Fact]
    public void Save_ThenRecreatingTheRegistryOnTheSameDirectory_StillFindsTheRecord()
    {
        var projectId = Guid.NewGuid();
        var imageId = Guid.NewGuid();
        var vectorId = Guid.NewGuid();
        var record = SampleRecord(projectId, imageId, vectorId);

        var firstRegistry = CreateRegistry();
        firstRegistry.Save(record);

        var secondRegistry = CreateRegistry();

        var found = secondRegistry.FindByVectorId(projectId, imageId, vectorId);
        Assert.NotNull(found);
        Assert.Equal(record.Components.Count, found!.Components.Count);
    }

    [Fact]
    public void NextVersion_AfterRecreatingTheRegistry_ContinuesFromTheHighestPersistedVersion()
    {
        var projectId = Guid.NewGuid();
        var imageId = Guid.NewGuid();
        var vectorId = Guid.NewGuid();

        var firstRegistry = CreateRegistry();
        firstRegistry.Save(SampleRecord(projectId, imageId, vectorId, version: 1));
        firstRegistry.Save(SampleRecord(projectId, imageId, vectorId, version: 2));

        var secondRegistry = CreateRegistry();

        Assert.Equal(3, secondRegistry.NextVersion(projectId, imageId));
    }

    [Fact]
    public void FindByVectorId_WhenRecordWasNeverSaved_ReturnsNull()
    {
        var registry = CreateRegistry();

        Assert.Null(registry.FindByVectorId(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));
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
