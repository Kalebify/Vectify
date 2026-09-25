using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vectify.Api.Clients;
using Vectify.Api.Contracts;
using Vectify.Api.Options;
using Vectify.Api.Simplification;
using Vectify.Api.Tests.Projects;
using Vectify.Api.Vectorization;

namespace Vectify.Api.Tests.Simplification;

/// <summary>
/// Pruebas unitarias de SimplificationService: orquesta localizar el SVG de
/// origen + validación + preview reversible (sin caché) + aplicar
/// (caché/lock/storage/versionado), sin depender de HTTP real ni de
/// Douglas-Peucker (eso lo cubren EndToEnd/SimplificationEndpointsTests y los
/// tests de Python). Mismo criterio que VectorizationServiceTests (M1-S05).
/// </summary>
public sealed class SimplificationServiceTests
{
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ImageId = Guid.NewGuid();
    private static readonly Guid SourceVectorId = Guid.NewGuid();
    private const string SourceStorageKey = "project/image/vectors/source.svg";

    private static (
        SimplificationService Service,
        FakeFileStorage Storage,
        InMemorySimplificationVersionRegistry SimplificationRegistry,
        FakePythonSimplifyClient PythonClient) CreateService(bool withExistingSourceVector = true)
    {
        var vectorizationService = new FakeVectorizationService();
        if (withExistingSourceVector)
        {
            vectorizationService.AddVector(new VectorVersion(
                ProjectId, ImageId, 1, SourceVectorId, Guid.NewGuid(),
                new VectorParameters(),
                SourceStorageKey, "image/svg+xml", 10, 10,
                new VectorMetrics(1, 12, new VectorBounds(0, 0, 10, 10, 10, 10)),
                DateTimeOffset.UtcNow));
        }

        var storage = new FakeFileStorage();
        storage.Saved[SourceStorageKey] = System.Text.Encoding.UTF8.GetBytes(
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\"><path d=\"M0,0 L10,0 L10,10 L0,10 Z\"/></svg>");

        var simplificationRegistry = new InMemorySimplificationVersionRegistry();
        var validator = new SimplificationParameterValidator(Microsoft.Extensions.Options.Options.Create(new SimplificationOptions()));
        var pythonClient = new FakePythonSimplifyClient();

        var service = new SimplificationService(
            vectorizationService, simplificationRegistry, validator, pythonClient, storage,
            NullLogger<SimplificationService>.Instance);

        return (service, storage, simplificationRegistry, pythonClient);
    }

    private static SimplifyRequest DefaultRequest(Guid? vectorId = null, string? preset = "low") =>
        new(vectorId ?? SourceVectorId, preset, null);

    // ---- PreviewAsync ----

    [Fact]
    public async Task PreviewAsync_WhenSourceVectorDoesNotExist_ReturnsNotFound()
    {
        var (service, _, _, _) = CreateService(withExistingSourceVector: false);

        var result = await service.PreviewAsync(ProjectId, ImageId, DefaultRequest(), CancellationToken.None);

        Assert.IsType<SimplificationPreviewResult.NotFound>(result);
    }

    [Fact]
    public async Task PreviewAsync_WhenParametersAreInvalid_ReturnsValidationFailedWithoutCallingPython()
    {
        var (service, _, _, pythonClient) = CreateService();

        var result = await service.PreviewAsync(ProjectId, ImageId, DefaultRequest(preset: "unknown"), CancellationToken.None);

        var failed = Assert.IsType<SimplificationPreviewResult.ValidationFailed>(result);
        Assert.Equal("invalid_parameters", failed.Code);
        Assert.Equal(0, pythonClient.CallCount);
    }

    [Fact]
    public async Task PreviewAsync_WhenSuccessful_ReturnsMetricsAndSvgWithoutPersisting()
    {
        var (service, storage, simplificationRegistry, pythonClient) = CreateService();
        var savedCountBefore = storage.Saved.Count;

        var result = await service.PreviewAsync(ProjectId, ImageId, DefaultRequest(), CancellationToken.None);

        var ready = Assert.IsType<SimplificationPreviewResult.Ready>(result);
        Assert.True(ready.Metrics.ReductionPercent > 0);
        Assert.Contains("<svg", ready.Svg);
        Assert.Equal(1, pythonClient.CallCount);

        // Reversible: nada persistido, ni en storage ni en el registro.
        Assert.Equal(savedCountBefore, storage.Saved.Count);
        Assert.Null(simplificationRegistry.FindLatest(ProjectId, ImageId));
    }

    [Fact]
    public async Task PreviewAsync_WhenCalledTwice_CallsPythonTwiceAndNeverCaches()
    {
        var (service, _, _, pythonClient) = CreateService();
        var request = DefaultRequest();

        await service.PreviewAsync(ProjectId, ImageId, request, CancellationToken.None);
        await service.PreviewAsync(ProjectId, ImageId, request, CancellationToken.None);

        // A diferencia de ApplyAsync, Preview NUNCA cachea -- cada llamada es
        // independiente y no debe dejar rastro (ver spec.md, "Preview es
        // reversible").
        Assert.Equal(2, pythonClient.CallCount);
    }

    // ---- ApplyAsync ----

    [Fact]
    public async Task ApplyAsync_WhenSourceVectorDoesNotExist_ReturnsNotFound()
    {
        var (service, _, _, _) = CreateService(withExistingSourceVector: false);

        var result = await service.ApplyAsync(ProjectId, ImageId, DefaultRequest(), CancellationToken.None);

        Assert.IsType<SimplificationResult.NotFound>(result);
    }

    [Fact]
    public async Task ApplyAsync_WhenCalledFirstTime_CreatesNewSimplificationAndSavesToStorage()
    {
        var (service, storage, _, pythonClient) = CreateService();

        var result = await service.ApplyAsync(ProjectId, ImageId, DefaultRequest(), CancellationToken.None);

        var ready = Assert.IsType<SimplificationResult.Ready>(result);
        Assert.False(ready.FromCache);
        Assert.Equal(1, ready.Record.Version);
        Assert.Equal(1, pythonClient.CallCount);
        Assert.True(storage.Saved.ContainsKey(ready.Record.SvgStorageKey));
        Assert.NotEqual(Guid.Empty, ready.Record.SimplificationId);
        Assert.Equal(SourceVectorId, ready.Record.SourceVectorId);
        Assert.True(ready.Record.Metrics.ReductionPercent > 0);
    }

    [Fact]
    public async Task ApplyAsync_NeverOverwritesTheSourceVectorSvgInStorage()
    {
        var (service, storage, _, _) = CreateService();
        var originalSourceBytes = storage.Saved[SourceStorageKey];

        var result = await service.ApplyAsync(ProjectId, ImageId, DefaultRequest(), CancellationToken.None);

        var ready = Assert.IsType<SimplificationResult.Ready>(result);
        Assert.NotEqual(SourceStorageKey, ready.Record.SvgStorageKey);
        Assert.Equal(originalSourceBytes, storage.Saved[SourceStorageKey]);
    }

    [Fact]
    public async Task ApplyAsync_WhenCalledAgainForSameVectorAndParams_ReturnsCachedWithoutCallingPythonAgain()
    {
        var (service, _, _, pythonClient) = CreateService();
        var request = DefaultRequest();

        var first = await service.ApplyAsync(ProjectId, ImageId, request, CancellationToken.None);
        var second = await service.ApplyAsync(ProjectId, ImageId, request, CancellationToken.None);

        var firstReady = Assert.IsType<SimplificationResult.Ready>(first);
        var secondReady = Assert.IsType<SimplificationResult.Ready>(second);
        Assert.False(firstReady.FromCache);
        Assert.True(secondReady.FromCache);
        Assert.Equal(firstReady.Record.SimplificationId, secondReady.Record.SimplificationId);
        Assert.Equal(firstReady.Record.Version + 1, secondReady.Record.Version);
        Assert.Equal(1, pythonClient.CallCount);
    }

    [Fact]
    public async Task ApplyAsync_WhenSamePresetButDifferentSourceVector_TreatsAsDifferentSimplification()
    {
        var otherVectorId = Guid.NewGuid();
        var vectorizationService = new FakeVectorizationService();
        vectorizationService.AddVector(new VectorVersion(
            ProjectId, ImageId, 1, SourceVectorId, Guid.NewGuid(), new VectorParameters(),
            SourceStorageKey, "image/svg+xml", 10, 10, new VectorMetrics(1, 12, new VectorBounds(0, 0, 10, 10, 10, 10)), DateTimeOffset.UtcNow));
        const string otherStorageKey = "project/image/vectors/other.svg";
        vectorizationService.AddVector(new VectorVersion(
            ProjectId, ImageId, 2, otherVectorId, Guid.NewGuid(), new VectorParameters(),
            otherStorageKey, "image/svg+xml", 20, 20, new VectorMetrics(1, 12, new VectorBounds(0, 0, 20, 20, 20, 20)), DateTimeOffset.UtcNow));

        var storage = new FakeFileStorage();
        storage.Saved[SourceStorageKey] = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\"><path d=\"M0,0 L10,0 L10,10 L0,10 Z\"/></svg>");
        storage.Saved[otherStorageKey] = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"20\" height=\"20\"><path d=\"M0,0 L20,0 L20,20 L0,20 Z\"/></svg>");

        var simplificationRegistry = new InMemorySimplificationVersionRegistry();
        var validator = new SimplificationParameterValidator(Microsoft.Extensions.Options.Options.Create(new SimplificationOptions()));
        var pythonClient = new FakePythonSimplifyClient();
        var service = new SimplificationService(
            vectorizationService, simplificationRegistry, validator, pythonClient, storage, NullLogger<SimplificationService>.Instance);

        var first = await service.ApplyAsync(ProjectId, ImageId, DefaultRequest(SourceVectorId), CancellationToken.None);
        var second = await service.ApplyAsync(ProjectId, ImageId, DefaultRequest(otherVectorId), CancellationToken.None);

        var firstReady = Assert.IsType<SimplificationResult.Ready>(first);
        var secondReady = Assert.IsType<SimplificationResult.Ready>(second);
        Assert.False(secondReady.FromCache);
        Assert.NotEqual(firstReady.Record.SimplificationId, secondReady.Record.SimplificationId);
        Assert.Equal(2, pythonClient.CallCount);
    }

    [Fact]
    public async Task ApplyAsync_WhenConcurrentRequestsForSameVectorAndParams_CallsPythonOnlyOnce()
    {
        var (service, _, simplificationRegistry, pythonClient) = CreateService();
        pythonClient.Delay = TimeSpan.FromMilliseconds(50);
        var request = DefaultRequest();

        var task1 = service.ApplyAsync(ProjectId, ImageId, request, CancellationToken.None);
        var task2 = service.ApplyAsync(ProjectId, ImageId, request, CancellationToken.None);
        var results = await Task.WhenAll(task1, task2);

        Assert.Equal(1, pythonClient.CallCount);

        var ready1 = Assert.IsType<SimplificationResult.Ready>(results[0]);
        var ready2 = Assert.IsType<SimplificationResult.Ready>(results[1]);
        Assert.Equal(ready1.Record.SimplificationId, ready2.Record.SimplificationId);

        var latest = simplificationRegistry.FindLatest(ProjectId, ImageId);
        Assert.NotNull(latest);
        Assert.Equal(ready1.Record.SimplificationId, latest!.SimplificationId);
    }

    [Fact]
    public async Task ApplyAsync_WhenPythonReportsInvalidInputSvg_ReturnsUpstreamErrorWithExpectedCode()
    {
        var (service, _, _, pythonClient) = CreateService();
        pythonClient.Respond = () => new PythonSimplifyResult(
            PythonSimplifyState.InvalidInputSvg, null, null, null, "SVG de entrada corrupto");

        var result = await service.ApplyAsync(ProjectId, ImageId, DefaultRequest(), CancellationToken.None);

        var error = Assert.IsType<SimplificationResult.UpstreamError>(result);
        Assert.Equal("invalid_input_svg", error.Code);
    }

    [Fact]
    public async Task ApplyAsync_WhenPythonTimesOut_ReturnsUpstreamErrorWithTimeoutCode()
    {
        var (service, _, _, pythonClient) = CreateService();
        pythonClient.Respond = () => new PythonSimplifyResult(
            PythonSimplifyState.Timeout, null, null, null, "tardó demasiado");

        var result = await service.ApplyAsync(ProjectId, ImageId, DefaultRequest(), CancellationToken.None);

        var error = Assert.IsType<SimplificationResult.UpstreamError>(result);
        Assert.Equal("timeout", error.Code);
    }

    [Fact]
    public async Task FindSimplification_WhenSimplificationWasApplied_ReturnsRecord()
    {
        var (service, _, _, _) = CreateService();
        var applied = await service.ApplyAsync(ProjectId, ImageId, DefaultRequest(), CancellationToken.None);
        var ready = Assert.IsType<SimplificationResult.Ready>(applied);

        var found = service.FindSimplification(ProjectId, ImageId, ready.Record.SimplificationId);

        Assert.NotNull(found);
        Assert.Equal(ready.Record.SimplificationId, found!.SimplificationId);
    }

    [Fact]
    public void FindSimplification_WhenSimplificationDoesNotExist_ReturnsNull()
    {
        var (service, _, _, _) = CreateService();

        var found = service.FindSimplification(ProjectId, ImageId, Guid.NewGuid());

        Assert.Null(found);
    }
}
