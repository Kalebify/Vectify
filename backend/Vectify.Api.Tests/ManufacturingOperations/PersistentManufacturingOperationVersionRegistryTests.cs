using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Vectify.Api.ManufacturingOperations;
using Vectify.Api.Options;

namespace Vectify.Api.Tests.ManufacturingOperations;

/// <summary>
/// Pruebas de PersistentManufacturingOperationVersionRegistry: guardar,
/// encontrar la última versión por paleta+versión confirmada (mismo
/// contrato que InMemoryManufacturingOperationVersionRegistry) y que el
/// historial sobrevive a "reiniciar el proceso" -- acá simulado recreando la
/// instancia apuntando al mismo directorio en disco. Mismo criterio que
/// PersistentComponentGroupVersionRegistryTests.
/// </summary>
public sealed class PersistentManufacturingOperationVersionRegistryTests : IDisposable
{
    private readonly string _rootPath =
        Path.Combine(Path.GetTempPath(), "vectify-manufacturing-operation-registry-tests-" + Guid.NewGuid().ToString("n"));

    private PersistentManufacturingOperationVersionRegistry CreateRegistry()
    {
        var environment = new FakeHostEnvironment { ContentRootPath = _rootPath };
        var options = Microsoft.Extensions.Options.Options.Create(new ManufacturingOperationRegistryOptions { RootPath = "manufacturing-operations" });
        return new PersistentManufacturingOperationVersionRegistry(options, environment, NullLogger<PersistentManufacturingOperationVersionRegistry>.Instance);
    }

    private static ManufacturingOperationSetVersion SampleRecord(
        Guid projectId, Guid imageId, Guid paletteId, int paletteVersion = 1, int version = 1) => new(
        ProjectId: projectId,
        ImageId: imageId,
        PaletteId: paletteId,
        PaletteVersion: paletteVersion,
        LayerSetId: Guid.NewGuid(),
        Version: version,
        Assignments: new List<ManufacturingOperationAssignment>
        {
            new(Guid.NewGuid(), ManufacturingOperationKind.Cut, DateTimeOffset.UtcNow),
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
        Assert.Single(found!.Assignments);
        Assert.Equal(record.Assignments[0].GroupId, found.Assignments[0].GroupId);
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
            _rootPath, "manufacturing-operations", projectId.ToString("N"), imageId.ToString("N"), paletteId.ToString("N"), "3.json");
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
        Assert.Equal(record.Assignments.Count, found!.Assignments.Count);
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

        // Simula que M2-S02 recalculó la paleta (nueva versión confirmada,
        // PaletteVersion=2): las asignaciones de la versión anterior NO
        // migran automáticamente -- ver spec.md, mismo criterio que M2-S05.
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
        public string ApplicationName { get; set; } = "Vectify.Api.Tests";
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
