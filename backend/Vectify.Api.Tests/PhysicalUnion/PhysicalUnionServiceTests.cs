using Microsoft.Extensions.Logging.Abstractions;
using Vectify.Api.Clients;
using Vectify.Api.Components;
using Vectify.Api.Contracts;
using Vectify.Api.PhysicalUnion;
using Vectify.Api.Vectorization;

namespace Vectify.Api.Tests.PhysicalUnion;

/// <summary>
/// Pruebas unitarias de PhysicalUnionService (M2-S06): validación de
/// selección/existencia de componentIds contra la ComponentSetVersion
/// vigente (mismo criterio que ComponentGroupService), preview NUNCA
/// persiste nada sin importar el resultado, confirm SÍ persiste una
/// VectorVersion nueva reutilizando el tipo existente (la anterior nunca se
/// destruye) más un registro de auditoría, y "nunca fingir unión": si Python
/// responde que la unión no fue geométricamente posible, ni preview ni
/// confirm persisten absolutamente nada.
/// </summary>
public sealed class PhysicalUnionServiceTests
{
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ImageId = Guid.NewGuid();
    private static readonly Guid VectorId = Guid.NewGuid();
    private const string SvgStorageKey = "svg-storage-key";

    private sealed record Harness(
        PhysicalUnionService Service,
        FakeComponentAnalysisService ComponentAnalysis,
        FakeVectorizationService Vectorization,
        InMemoryVectorVersionRegistry VectorRegistry,
        InMemoryPhysicalUnionVersionRegistry UnionRegistry,
        FakePythonPhysicalUnionClient PythonClient,
        FakeFileStorage FileStorage,
        ComponentSetVersion ComponentSet,
        VectorVersion SourceVector);

    private static Harness CreateService(IReadOnlyList<string>? componentIds = null)
    {
        var ids = componentIds ?? new[] { "component-1", "component-2" };
        var componentSet = new ComponentSetVersion(
            ProjectId,
            ImageId,
            Version: 1,
            ComponentSetId: Guid.NewGuid(),
            VectorId: VectorId,
            Components: ids.Select((id, index) => new LayerComponent(
                id,
                new List<ComponentMember> { new(index, 0, "solid", new ComponentBounds(0, 0, 10, 10), 100) },
                new ComponentBounds(0, 0, 10, 10),
                100,
                false)).ToList(),
            SkippedPathCount: 0,
            CreatedAt: DateTimeOffset.UtcNow);

        var componentAnalysis = new FakeComponentAnalysisService();
        componentAnalysis.AddComponentSet(componentSet);

        var sourceVector = new VectorVersion(
            ProjectId,
            ImageId,
            Version: 1,
            VectorId: VectorId,
            SourceMaskId: Guid.NewGuid(),
            Parameters: new VectorParameters(),
            SvgStorageKey: SvgStorageKey,
            ContentType: "image/svg+xml",
            Width: 100,
            Height: 100,
            Metrics: new VectorMetrics(2, 8, new VectorBounds(0, 0, 100, 100, 100, 100)),
            CreatedAt: DateTimeOffset.UtcNow);

        var vectorization = new FakeVectorizationService();
        vectorization.AddVector(sourceVector);

        var fileStorage = new FakeFileStorage();
        fileStorage.Seed(SvgStorageKey, "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"100\" height=\"100\"></svg>");

        var vectorRegistry = new InMemoryVectorVersionRegistry();
        vectorRegistry.Save(sourceVector);

        var unionRegistry = new InMemoryPhysicalUnionVersionRegistry();
        var pythonClient = new FakePythonPhysicalUnionClient();

        var service = new PhysicalUnionService(
            componentAnalysis, vectorization, vectorRegistry, unionRegistry, pythonClient, fileStorage,
            NullLogger<PhysicalUnionService>.Instance);

        return new Harness(service, componentAnalysis, vectorization, vectorRegistry, unionRegistry, pythonClient, fileStorage, componentSet, sourceVector);
    }

    // ---- Validación de selección ----

    [Fact]
    public async Task PreviewAsync_WithFewerThanTwoDistinctComponentIds_ReturnsValidationFailed()
    {
        var harness = CreateService();

        var result = await harness.Service.PreviewAsync(
            ProjectId, ImageId, VectorId, new PhysicalUnionRequest(new[] { "component-1" }), CancellationToken.None);

        var failed = Assert.IsType<PhysicalUnionPreviewResult.ValidationFailed>(result);
        Assert.Equal("invalid_parameters", failed.Code);
        Assert.Equal(0, harness.PythonClient.CallCount);
    }

    [Fact]
    public async Task PreviewAsync_WithDuplicateComponentIdsOnly_ReturnsValidationFailed()
    {
        var harness = CreateService();

        var result = await harness.Service.PreviewAsync(
            ProjectId, ImageId, VectorId, new PhysicalUnionRequest(new[] { "component-1", "component-1" }), CancellationToken.None);

        Assert.IsType<PhysicalUnionPreviewResult.ValidationFailed>(result);
    }

    [Fact]
    public async Task PreviewAsync_WhenNoComponentAnalysisExistsForVector_ReturnsNotFound()
    {
        var harness = CreateService();
        var otherVectorId = Guid.NewGuid();

        var result = await harness.Service.PreviewAsync(
            ProjectId, ImageId, otherVectorId, new PhysicalUnionRequest(new[] { "component-1", "component-2" }), CancellationToken.None);

        var notFound = Assert.IsType<PhysicalUnionPreviewResult.NotFound>(result);
        Assert.Equal("not_found", notFound.Code);
    }

    [Fact]
    public async Task PreviewAsync_WhenAComponentIdDoesNotExist_ReturnsNotFound()
    {
        var harness = CreateService();

        var result = await harness.Service.PreviewAsync(
            ProjectId, ImageId, VectorId, new PhysicalUnionRequest(new[] { "component-1", "does-not-exist" }), CancellationToken.None);

        var notFound = Assert.IsType<PhysicalUnionPreviewResult.NotFound>(result);
        Assert.Equal("component_not_found", notFound.Code);
    }

    // ---- Preview nunca persiste nada ----

    [Fact]
    public async Task PreviewAsync_WhenSuccessful_ReturnsOutcomeWithoutPersistingAnyVectorVersion()
    {
        var harness = CreateService();

        var result = await harness.Service.PreviewAsync(
            ProjectId, ImageId, VectorId, new PhysicalUnionRequest(new[] { "component-1", "component-2" }), CancellationToken.None);

        var ready = Assert.IsType<PhysicalUnionPreviewResult.Ready>(result);
        Assert.Equal(1, ready.Outcome.ComponentCountAfter);
        Assert.Equal("bridge", ready.Outcome.Strategy);

        // El registro de vectorización sigue apuntando EXACTAMENTE a la
        // misma VectorVersion de origen -- Preview nunca llama a
        // IVectorVersionRegistry.Save, así que "la última versión de la
        // imagen" no cambió.
        Assert.Equal(harness.SourceVector, harness.VectorRegistry.FindLatest(ProjectId, ImageId));
        // Ningún archivo nuevo se guardó en storage (solo el SVG de origen, sembrado de antemano).
        Assert.Single(harness.FileStorage.Saved);
        Assert.Null(harness.UnionRegistry.FindLatest(ProjectId, ImageId, VectorId));
    }

    [Fact]
    public async Task PreviewAsync_WhenPythonReportsGeometryImpossible_ReturnsGeometryImpossibleAndPersistsNothing()
    {
        var harness = CreateService();
        harness.PythonClient.Respond = () => new PythonPhysicalUnionResult(
            PythonPhysicalUnionState.Impossible, null, null, null, null, null, null, null, null, null,
            "Tras la unión, el análisis de componentes detectó 2 pieza(s) en vez de la 1 esperada.");

        var result = await harness.Service.PreviewAsync(
            ProjectId, ImageId, VectorId, new PhysicalUnionRequest(new[] { "component-1", "component-2" }), CancellationToken.None);

        var impossible = Assert.IsType<PhysicalUnionPreviewResult.GeometryImpossible>(result);
        Assert.Equal("physical_union_impossible", impossible.Code);
        Assert.Null(harness.UnionRegistry.FindLatest(ProjectId, ImageId, VectorId));
    }

    [Fact]
    public async Task PreviewAsync_WhenPythonReportsInvalidGeometry_ReturnsGeometryImpossible()
    {
        var harness = CreateService();
        harness.PythonClient.Respond = () => new PythonPhysicalUnionResult(
            PythonPhysicalUnionState.InvalidGeometry, null, null, null, null, null, null, null, null, null,
            "El componente 'component-1' tiene geometría autointersectante.");

        var result = await harness.Service.PreviewAsync(
            ProjectId, ImageId, VectorId, new PhysicalUnionRequest(new[] { "component-1", "component-2" }), CancellationToken.None);

        var impossible = Assert.IsType<PhysicalUnionPreviewResult.GeometryImpossible>(result);
        Assert.Equal("physical_union_invalid_geometry", impossible.Code);
    }

    // ---- Confirm persiste una VectorVersion nueva ----

    [Fact]
    public async Task ConfirmAsync_WhenSuccessful_PersistsANewVectorVersionAndKeepsThePreviousOneIntact()
    {
        var harness = CreateService();

        var result = await harness.Service.ConfirmAsync(
            ProjectId, ImageId, VectorId, new PhysicalUnionRequest(new[] { "component-1", "component-2" }), CancellationToken.None);

        var ready = Assert.IsType<PhysicalUnionConfirmResult.Ready>(result);
        Assert.NotEqual(VectorId, ready.NewVector.VectorId);
        Assert.Equal(100, ready.NewVector.Width);

        // La VectorVersion anterior sigue existiendo, intacta, en el registro.
        var previous = harness.VectorRegistry.FindByVectorId(ProjectId, ImageId, VectorId);
        Assert.NotNull(previous);
        Assert.Equal(harness.SourceVector, previous);

        // La nueva quedó persistida y es recuperable por su propio VectorId.
        var persistedNew = harness.VectorRegistry.FindByVectorId(ProjectId, ImageId, ready.NewVector.VectorId);
        Assert.NotNull(persistedNew);
        Assert.True(harness.FileStorage.Saved.ContainsKey(persistedNew!.SvgStorageKey));
    }

    [Fact]
    public async Task ConfirmAsync_WhenSuccessful_SavesAnAuditRecordKeyedBySourceVectorId()
    {
        var harness = CreateService();

        var result = await harness.Service.ConfirmAsync(
            ProjectId, ImageId, VectorId, new PhysicalUnionRequest(new[] { "component-1", "component-2" }), CancellationToken.None);

        var ready = Assert.IsType<PhysicalUnionConfirmResult.Ready>(result);
        var audit = harness.UnionRegistry.FindLatest(ProjectId, ImageId, VectorId);
        Assert.NotNull(audit);
        Assert.Equal(VectorId, audit!.SourceVectorId);
        Assert.Equal(ready.NewVector.VectorId, audit.ResultVectorId);
        Assert.Equal(2, audit.ComponentIds.Count);
        Assert.Equal("bridge", audit.Strategy);
        Assert.Equal(1, audit.ResultComponentCount);
    }

    [Fact]
    public async Task ConfirmAsync_WhenGeometryImpossible_PersistsNothingAtAll()
    {
        var harness = CreateService();
        harness.PythonClient.Respond = () => new PythonPhysicalUnionResult(
            PythonPhysicalUnionState.Impossible, null, null, null, null, null, null, null, null, null,
            "No fue geométricamente posible.");

        var result = await harness.Service.ConfirmAsync(
            ProjectId, ImageId, VectorId, new PhysicalUnionRequest(new[] { "component-1", "component-2" }), CancellationToken.None);

        Assert.IsType<PhysicalUnionConfirmResult.GeometryImpossible>(result);
        Assert.Null(harness.UnionRegistry.FindLatest(ProjectId, ImageId, VectorId));
        Assert.Equal(new[] { SvgStorageKey }, harness.FileStorage.Saved.Keys);

        // La VectorVersion original sigue siendo la única/última para ese VectorId.
        var previous = harness.VectorRegistry.FindByVectorId(ProjectId, ImageId, VectorId);
        Assert.Equal(harness.SourceVector, previous);
    }

    [Fact]
    public async Task ConfirmAsync_WhenSelectionIsInvalid_PersistsNothingAndNeverCallsPython()
    {
        var harness = CreateService();

        var result = await harness.Service.ConfirmAsync(
            ProjectId, ImageId, VectorId, new PhysicalUnionRequest(new[] { "component-1" }), CancellationToken.None);

        Assert.IsType<PhysicalUnionConfirmResult.ValidationFailed>(result);
        Assert.Equal(0, harness.PythonClient.CallCount);
        Assert.Null(harness.UnionRegistry.FindLatest(ProjectId, ImageId, VectorId));
    }

    [Fact]
    public async Task ConfirmAsync_PassesTheSelectedComponentsMembersToPython()
    {
        var harness = CreateService(new[] { "component-1", "component-2", "component-3" });

        await harness.Service.ConfirmAsync(
            ProjectId, ImageId, VectorId, new PhysicalUnionRequest(new[] { "component-1", "component-3" }), CancellationToken.None);

        Assert.NotNull(harness.PythonClient.LastSelectedComponents);
        Assert.Equal(2, harness.PythonClient.LastSelectedComponents!.Count);
        Assert.Contains(harness.PythonClient.LastSelectedComponents, c => c.Id == "component-1");
        Assert.Contains(harness.PythonClient.LastSelectedComponents, c => c.Id == "component-3");
    }
}
