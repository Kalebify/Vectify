using Microsoft.Extensions.Logging.Abstractions;
using Vectify.Api.Clients;
using Vectify.Api.Contracts;
using Vectify.Api.Tests.Projects;
using Vectify.Api.Threshold;
using Vectify.Api.Vectorization;

namespace Vectify.Api.Tests.Vectorization;

/// <summary>
/// Pruebas unitarias de VectorizationService: orquesta localizar la máscara
/// de origen + validación + cache + llamada a Python + storage + versionado,
/// sin depender de HTTP real ni de VTracer (eso lo cubren
/// EndToEnd/VectorizationEndpointsTests y los tests de Python). Mismo
/// criterio que ThresholdServiceTests (M1-S04).
/// </summary>
public sealed class VectorizationServiceTests
{
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ImageId = Guid.NewGuid();
    private static readonly Guid SourceMaskId = Guid.NewGuid();
    private const string SourceStorageKey = "project/image/masks/source.png";

    private static (VectorizationService Service, FakeFileStorage Storage, InMemoryVectorVersionRegistry VectorRegistry, FakePythonVectorizeClient PythonClient) CreateService(
        bool withExistingSourceMask = true)
    {
        var thresholdService = new FakeThresholdService();
        if (withExistingSourceMask)
        {
            thresholdService.AddMask(new ThresholdConfigRecord(
                ProjectId, ImageId, 1, SourceMaskId, Guid.NewGuid(),
                new ThresholdParameters(128, false),
                SourceStorageKey, "image/png", 10, 10,
                new ThresholdMetrics(40.0, 60.0, false, false, null, null),
                DateTimeOffset.UtcNow));
        }

        var storage = new FakeFileStorage();
        storage.Saved[SourceStorageKey] = [9, 9, 9, 9];

        var vectorRegistry = new InMemoryVectorVersionRegistry();
        var validator = new VectorParameterValidator();
        var pythonClient = new FakePythonVectorizeClient();

        var service = new VectorizationService(
            thresholdService, vectorRegistry, validator, pythonClient, storage, NullLogger<VectorizationService>.Instance);

        return (service, storage, vectorRegistry, pythonClient);
    }

    private static VectorizeRequest DefaultRequest(Guid? maskId = null) => new(maskId ?? SourceMaskId);

    [Fact]
    public async Task GenerateVectorAsync_WhenSourceMaskDoesNotExist_ReturnsNotFound()
    {
        var (service, _, _, _) = CreateService(withExistingSourceMask: false);

        var result = await service.GenerateVectorAsync(ProjectId, ImageId, DefaultRequest(), CancellationToken.None);

        Assert.IsType<VectorResult.NotFound>(result);
    }

    [Fact]
    public async Task GenerateVectorAsync_WhenMaskIdIsEmpty_ReturnsValidationFailedWithoutCallingPython()
    {
        var (service, _, _, pythonClient) = CreateService();

        var result = await service.GenerateVectorAsync(ProjectId, ImageId, DefaultRequest(Guid.Empty), CancellationToken.None);

        var failed = Assert.IsType<VectorResult.ValidationFailed>(result);
        Assert.Equal("invalid_parameters", failed.Code);
        Assert.Equal(0, pythonClient.CallCount);
    }

    [Fact]
    public async Task GenerateVectorAsync_WhenCalledFirstTime_GeneratesNewVectorAndSavesToStorage()
    {
        var (service, storage, _, pythonClient) = CreateService();

        var result = await service.GenerateVectorAsync(ProjectId, ImageId, DefaultRequest(), CancellationToken.None);

        var ready = Assert.IsType<VectorResult.Ready>(result);
        Assert.False(ready.FromCache);
        Assert.Equal(1, ready.Record.Version);
        Assert.Equal(1, pythonClient.CallCount);
        Assert.True(storage.Saved.ContainsKey(ready.Record.SvgStorageKey));
        Assert.NotEqual(Guid.Empty, ready.Record.VectorId);
        Assert.Equal(SourceMaskId, ready.Record.SourceMaskId);
        Assert.Equal(1, ready.Record.Metrics.PathCount);
    }

    [Fact]
    public async Task GenerateVectorAsync_WhenCalledAgainForSameMask_ReturnsCachedWithoutCallingPythonAgain()
    {
        var (service, _, _, pythonClient) = CreateService();
        var request = DefaultRequest();

        var first = await service.GenerateVectorAsync(ProjectId, ImageId, request, CancellationToken.None);
        var second = await service.GenerateVectorAsync(ProjectId, ImageId, request, CancellationToken.None);

        var firstReady = Assert.IsType<VectorResult.Ready>(first);
        var secondReady = Assert.IsType<VectorResult.Ready>(second);
        Assert.False(firstReady.FromCache);
        Assert.True(secondReady.FromCache);
        Assert.Equal(firstReady.Record.VectorId, secondReady.Record.VectorId);
        Assert.Equal(firstReady.Record.SvgStorageKey, secondReady.Record.SvgStorageKey);
        Assert.Equal(firstReady.Record.Version + 1, secondReady.Record.Version);
        Assert.Equal(1, pythonClient.CallCount);
    }

    [Fact]
    public async Task GenerateVectorAsync_WhenRetriedIdempotently_DoesNotCreateDuplicateVectors()
    {
        // "Reintentos son razonablemente idempotentes" -- ver spec.md, criterios
        // de aceptación: reintentar sobre la misma máscara de origen no debería
        // producir vectorizaciones duplicadas (archivos distintos en storage).
        var (service, storage, vectorRegistry, pythonClient) = CreateService();
        var request = DefaultRequest();

        await service.GenerateVectorAsync(ProjectId, ImageId, request, CancellationToken.None);
        await service.GenerateVectorAsync(ProjectId, ImageId, request, CancellationToken.None);
        var third = await service.GenerateVectorAsync(ProjectId, ImageId, request, CancellationToken.None);

        Assert.Equal(1, pythonClient.CallCount);
        var svgFileCount = storage.Saved.Keys.Count(key => key.Contains("/vectors/"));
        Assert.Equal(1, svgFileCount);

        var latest = vectorRegistry.FindLatest(ProjectId, ImageId);
        Assert.NotNull(latest);
        Assert.Equal(3, latest!.Version);
        Assert.Equal(((VectorResult.Ready)third).Record.VectorId, latest.VectorId);
    }

    [Fact]
    public async Task GenerateVectorAsync_WhenConcurrentRequestsForSameMask_CallsPythonOnlyOnce()
    {
        var (service, _, vectorRegistry, pythonClient) = CreateService();
        pythonClient.Delay = TimeSpan.FromMilliseconds(50);
        var request = DefaultRequest();

        var task1 = service.GenerateVectorAsync(ProjectId, ImageId, request, CancellationToken.None);
        var task2 = service.GenerateVectorAsync(ProjectId, ImageId, request, CancellationToken.None);
        var results = await Task.WhenAll(task1, task2);

        Assert.Equal(1, pythonClient.CallCount);

        var ready1 = Assert.IsType<VectorResult.Ready>(results[0]);
        var ready2 = Assert.IsType<VectorResult.Ready>(results[1]);
        Assert.Equal(ready1.Record.VectorId, ready2.Record.VectorId);

        var latest = vectorRegistry.FindLatest(ProjectId, ImageId);
        Assert.NotNull(latest);
        Assert.Equal(ready1.Record.VectorId, latest!.VectorId);
    }

    [Fact]
    public async Task GenerateVectorAsync_WhenSameParamsButDifferentSourceMask_TreatsAsDifferentVector()
    {
        var otherMaskId = Guid.NewGuid();
        var thresholdService = new FakeThresholdService();
        thresholdService.AddMask(new ThresholdConfigRecord(
            ProjectId, ImageId, 1, SourceMaskId, Guid.NewGuid(), new ThresholdParameters(128, false),
            SourceStorageKey, "image/png", 10, 10, new ThresholdMetrics(40.0, 60.0, false, false, null, null), DateTimeOffset.UtcNow));
        thresholdService.AddMask(new ThresholdConfigRecord(
            ProjectId, ImageId, 2, otherMaskId, Guid.NewGuid(), new ThresholdParameters(200, true),
            SourceStorageKey, "image/png", 10, 10, new ThresholdMetrics(80.0, 20.0, false, false, null, null), DateTimeOffset.UtcNow));

        var storage = new FakeFileStorage();
        storage.Saved[SourceStorageKey] = [9, 9, 9, 9];
        var vectorRegistry = new InMemoryVectorVersionRegistry();
        var validator = new VectorParameterValidator();
        var pythonClient = new FakePythonVectorizeClient();
        var service = new VectorizationService(
            thresholdService, vectorRegistry, validator, pythonClient, storage, NullLogger<VectorizationService>.Instance);

        var first = await service.GenerateVectorAsync(ProjectId, ImageId, DefaultRequest(SourceMaskId), CancellationToken.None);
        var second = await service.GenerateVectorAsync(ProjectId, ImageId, DefaultRequest(otherMaskId), CancellationToken.None);

        var firstReady = Assert.IsType<VectorResult.Ready>(first);
        var secondReady = Assert.IsType<VectorResult.Ready>(second);
        Assert.False(secondReady.FromCache); // no cachea entre máscaras de origen distintas
        Assert.NotEqual(firstReady.Record.VectorId, secondReady.Record.VectorId);
        Assert.Equal(2, pythonClient.CallCount);
    }

    [Fact]
    public async Task GenerateVectorAsync_WhenPythonReportsEmptyMask_ReturnsUpstreamErrorWithEmptyMaskCode()
    {
        var (service, _, _, pythonClient) = CreateService();
        pythonClient.Respond = () => new PythonVectorizeResult(
            PythonVectorizeState.EmptyMask, null, null, null, null, null, "sin foreground");

        var result = await service.GenerateVectorAsync(ProjectId, ImageId, DefaultRequest(), CancellationToken.None);

        var error = Assert.IsType<VectorResult.UpstreamError>(result);
        Assert.Equal("empty_mask", error.Code);
    }

    [Fact]
    public async Task GenerateVectorAsync_WhenPythonTimesOut_ReturnsUpstreamErrorWithTimeoutCode()
    {
        var (service, _, _, pythonClient) = CreateService();
        pythonClient.Respond = () => new PythonVectorizeResult(
            PythonVectorizeState.Timeout, null, null, null, null, null, "tardó demasiado");

        var result = await service.GenerateVectorAsync(ProjectId, ImageId, DefaultRequest(), CancellationToken.None);

        var error = Assert.IsType<VectorResult.UpstreamError>(result);
        Assert.Equal("timeout", error.Code);
    }

    [Fact]
    public async Task GenerateVectorAsync_WhenPythonIsUnavailable_ReturnsUpstreamErrorWithEngineUnavailableCode()
    {
        var (service, _, _, pythonClient) = CreateService();
        pythonClient.Respond = () => new PythonVectorizeResult(
            PythonVectorizeState.Unavailable, null, null, null, null, null, "sin conexión");

        var result = await service.GenerateVectorAsync(ProjectId, ImageId, DefaultRequest(), CancellationToken.None);

        var error = Assert.IsType<VectorResult.UpstreamError>(result);
        Assert.Equal("engine_unavailable", error.Code);
    }

    [Fact]
    public async Task FindVector_WhenVectorWasGenerated_ReturnsRecord()
    {
        var (service, _, _, _) = CreateService();
        var generated = await service.GenerateVectorAsync(ProjectId, ImageId, DefaultRequest(), CancellationToken.None);
        var ready = Assert.IsType<VectorResult.Ready>(generated);

        var found = service.FindVector(ProjectId, ImageId, ready.Record.VectorId);

        Assert.NotNull(found);
        Assert.Equal(ready.Record.VectorId, found!.VectorId);
    }

    [Fact]
    public void FindVector_WhenVectorDoesNotExist_ReturnsNull()
    {
        var (service, _, _, _) = CreateService();

        var found = service.FindVector(ProjectId, ImageId, Guid.NewGuid());

        Assert.Null(found);
    }
}
