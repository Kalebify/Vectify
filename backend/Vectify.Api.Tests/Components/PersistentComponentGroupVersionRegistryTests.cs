using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Vectify.Api.Components;
using Vectify.Api.Options;

namespace Vectify.Api.Tests.Components;

/// <summary>
/// Pruebas de PersistentComponentGroupVersionRegistry: guardar, encontrar la
/// última versión por VectorId (mismo contrato que
/// InMemoryComponentGroupVersionRegistry) y que el historial sobrevive a
/// "reiniciar el proceso" -- acá simulado recreando la instancia apuntando al
/// mismo directorio en disco. Mismo criterio que
/// PersistentComponentVersionRegistryTests.
/// </summary>
public sealed class PersistentComponentGroupVersionRegistryTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "vectify-component-group-registry-tests-" + Guid.NewGuid().ToString("n"));

    private PersistentComponentGroupVersionRegistry CreateRegistry()
    {
        var environment = new FakeHostEnvironment { ContentRootPath = _rootPath };
        var options = Microsoft.Extensions.Options.Options.Create(new ComponentGroupRegistryOptions { RootPath = "component-groups" });
        return new PersistentComponentGroupVersionRegistry(options, environment, NullLogger<PersistentComponentGroupVersionRegistry>.Instance);
    }

    private static ComponentGroupSetVersion SampleRecord(
        Guid projectId, Guid imageId, Guid vectorId, int version = 1) => new(
        ProjectId: projectId,
        ImageId: imageId,
        VectorId: vectorId,
        Version: version,
        Groups: new List<ComponentGroup>
        {
            new(Guid.NewGuid(), "Grupo 1", new List<string> { "component-1", "component-2" }, Guid.NewGuid(), DateTimeOffset.UtcNow),
        },
        CreatedAt: DateTimeOffset.UtcNow);

    [Fact]
    public void Save_ThenFindLatest_ReturnsTheSameRecordFromMemory()
    {
        var registry = CreateRegistry();
        var projectId = Guid.NewGuid();
        var imageId = Guid.NewGuid();
        var vectorId = Guid.NewGuid();
        var record = SampleRecord(projectId, imageId, vectorId);

        registry.Save(record);

        var found = registry.FindLatest(projectId, imageId, vectorId);
        Assert.NotNull(found);
        Assert.Single(found!.Groups);
        Assert.Equal(record.Groups[0].GroupId, found.Groups[0].GroupId);
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
            _rootPath, "component-groups", projectId.ToString("N"), imageId.ToString("N"), $"{vectorId:N}.json");
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

        var found = secondRegistry.FindLatest(projectId, imageId, vectorId);
        Assert.NotNull(found);
        Assert.Equal(record.Groups.Count, found!.Groups.Count);
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

        Assert.Equal(3, secondRegistry.NextVersion(projectId, imageId, vectorId));
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
