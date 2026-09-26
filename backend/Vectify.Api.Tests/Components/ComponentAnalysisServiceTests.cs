using Microsoft.Extensions.Logging.Abstractions;
using Vectify.Api.Clients;
using Vectify.Api.Components;
using Vectify.Api.Contracts;
using Vectify.Api.Tests.Projects;
using Vectify.Api.Vectorization;

namespace Vectify.Api.Tests.Components;

/// <summary>
/// Pruebas unitarias de ComponentAnalysisService: precondición (debe existir
/// una VectorVersion con ese VectorId, sin llamar a Python si no), ciclo
/// cache/lock/versionado (mismo criterio que DimensionService/
/// VectorLayerService -- acá cacheado por VectorId solo, que es inmutable) y
/// mapeo de errores upstream. Ver spec.md M2-S03, criterio de aceptación:
/// "no recalcular en cada request de lectura si ya se calculó para esa
/// combinación layer+versión".
/// </summary>
public sealed class ComponentAnalysisServiceTests
{
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ImageId = Guid.NewGuid();
    private static readonly Guid VectorId = Guid.NewGuid();
    private const string VectorStorageKey = "project/image/vectors/source.svg";
    private const string SourceSvg =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\"><path d=\"M2,2 L8,2 L8,8 L2,8 Z\"/></svg>";

    private static (
        ComponentAnalysisService Service,
        FakeFileStorage Storage,
        InMemoryComponentVersionRegistry Registry,
        FakePythonComponentClient PythonClient) CreateService(bool withExistingVector = true)
    {
        var vectorizationService = new FakeVectorizationService();
        if (withExistingVector)
        {
            vectorizationService.AddVector(new VectorVersion(
                ProjectId, ImageId, 1, VectorId, Guid.NewGuid(), new VectorParameters(),
                VectorStorageKey, "image/svg+xml", 10, 10,
                new VectorMetrics(1, 4, new VectorBounds(2, 2, 8, 8, 6, 6)), DateTimeOffset.UtcNow));
        }

        var storage = new FakeFileStorage();
        storage.Saved[VectorStorageKey] = System.Text.Encoding.UTF8.GetBytes(SourceSvg);

        var registry = new InMemoryComponentVersionRegistry();
        var pythonClient = new FakePythonComponentClient();

        var service = new ComponentAnalysisService(
            vectorizationService, registry, pythonClient, storage, NullLogger<ComponentAnalysisService>.Instance);

        return (service, storage, registry, pythonClient);
    }

    // ---- Precondición ----

    [Fact]
    public async Task AnalyzeAsync_WhenVectorDoesNotExist_ReturnsNotFoundWithoutCallingPython()
    {
        var (service, _, _, pythonClient) = CreateService(withExistingVector: false);

        var result = await service.AnalyzeAsync(ProjectId, ImageId, VectorId, CancellationToken.None);

        Assert.IsType<ComponentSetResult.NotFound>(result);
        Assert.Equal(0, pythonClient.CallCount);
    }

    // ---- Cálculo exitoso ----

    [Fact]
    public async Task AnalyzeAsync_WhenSuccessful_CreatesFirstVersionWithComponentsFromPython()
    {
        var (service, _, registry, pythonClient) = CreateService();

        var result = await service.AnalyzeAsync(ProjectId, ImageId, VectorId, CancellationToken.None);

        var ready = Assert.IsType<ComponentSetResult.Ready>(result);
        Assert.False(ready.FromCache);
        Assert.Equal(1, ready.Record.Version);
        Assert.Equal(VectorId, ready.Record.VectorId);
        Assert.Single(ready.Record.Components);
        Assert.Equal(1, pythonClient.CallCount);
        Assert.NotNull(registry.FindByVectorId(ProjectId, ImageId, VectorId));
    }

    // ---- Ciclo cache/versión ----

    [Fact]
    public async Task AnalyzeAsync_WhenCalledAgainForSameVectorId_ReturnsCachedWithoutCallingPythonAgain()
    {
        var (service, _, _, pythonClient) = CreateService();

        var first = await service.AnalyzeAsync(ProjectId, ImageId, VectorId, CancellationToken.None);
        var second = await service.AnalyzeAsync(ProjectId, ImageId, VectorId, CancellationToken.None);

        var firstReady = Assert.IsType<ComponentSetResult.Ready>(first);
        var secondReady = Assert.IsType<ComponentSetResult.Ready>(second);

        Assert.False(firstReady.FromCache);
        Assert.True(secondReady.FromCache);
        Assert.Equal(1, pythonClient.CallCount);

        // Nunca retrocede ni "re-sirve" la misma versión: siempre avanza,
        // reutilizando el mismo ComponentSetId y los mismos componentes ya calculados.
        Assert.Equal(firstReady.Record.Version + 1, secondReady.Record.Version);
        Assert.Equal(firstReady.Record.ComponentSetId, secondReady.Record.ComponentSetId);
        Assert.Equal(firstReady.Record.Components, secondReady.Record.Components);
    }

    [Fact]
    public async Task AnalyzeAsync_WhenConcurrentRequestsForSameVectorId_CallsPythonOnlyOnce()
    {
        var (service, _, registry, pythonClient) = CreateService();
        pythonClient.Delay = TimeSpan.FromMilliseconds(50);

        var task1 = service.AnalyzeAsync(ProjectId, ImageId, VectorId, CancellationToken.None);
        var task2 = service.AnalyzeAsync(ProjectId, ImageId, VectorId, CancellationToken.None);
        await Task.WhenAll(task1, task2);

        Assert.Equal(1, pythonClient.CallCount);
        Assert.NotNull(registry.FindByVectorId(ProjectId, ImageId, VectorId));
    }

    // ---- Errores upstream ----

    [Fact]
    public async Task AnalyzeAsync_WhenPythonTimesOut_ReturnsUpstreamErrorWithTimeoutCode()
    {
        var (service, _, _, pythonClient) = CreateService();
        pythonClient.Respond = () => new PythonComponentResult(PythonComponentState.Timeout, null, null, "tardó demasiado");

        var result = await service.AnalyzeAsync(ProjectId, ImageId, VectorId, CancellationToken.None);

        var error = Assert.IsType<ComponentSetResult.UpstreamError>(result);
        Assert.Equal("timeout", error.Code);
    }

    [Fact]
    public async Task AnalyzeAsync_WhenPythonIsUnavailable_ReturnsUpstreamErrorWithEngineUnavailableCode()
    {
        var (service, _, _, pythonClient) = CreateService();
        pythonClient.Respond = () => new PythonComponentResult(PythonComponentState.Unavailable, null, null, "sin conexión");

        var result = await service.AnalyzeAsync(ProjectId, ImageId, VectorId, CancellationToken.None);

        var error = Assert.IsType<ComponentSetResult.UpstreamError>(result);
        Assert.Equal("engine_unavailable", error.Code);
    }

    [Fact]
    public async Task AnalyzeAsync_WhenSourceSvgIsMissingFromStorage_ReturnsUpstreamErrorWithStorageFailureCode()
    {
        var (service, storage, _, pythonClient) = CreateService();
        storage.Saved.Remove(VectorStorageKey);

        var result = await service.AnalyzeAsync(ProjectId, ImageId, VectorId, CancellationToken.None);

        var error = Assert.IsType<ComponentSetResult.UpstreamError>(result);
        Assert.Equal("storage_failure", error.Code);
        Assert.Equal(0, pythonClient.CallCount);
    }

    // ---- FindLatest ----

    [Fact]
    public async Task FindLatest_AfterAnalyzing_ReturnsTheRecord()
    {
        var (service, _, _, _) = CreateService();
        var analyzed = await service.AnalyzeAsync(ProjectId, ImageId, VectorId, CancellationToken.None);
        var ready = Assert.IsType<ComponentSetResult.Ready>(analyzed);

        var found = service.FindLatest(ProjectId, ImageId, VectorId);

        Assert.NotNull(found);
        Assert.Equal(ready.Record.ComponentSetId, found!.ComponentSetId);
    }

    [Fact]
    public void FindLatest_WhenNeverAnalyzed_ReturnsNull()
    {
        var (service, _, _, _) = CreateService();

        var found = service.FindLatest(ProjectId, ImageId, Guid.NewGuid());

        Assert.Null(found);
    }
}
