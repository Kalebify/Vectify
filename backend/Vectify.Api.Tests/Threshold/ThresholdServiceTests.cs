using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vectify.Api.Clients;
using Vectify.Api.Contracts;
using Vectify.Api.Options;
using Vectify.Api.Preprocessing;
using Vectify.Api.Tests.Projects;
using Vectify.Api.Threshold;

namespace Vectify.Api.Tests.Threshold;

/// <summary>
/// Pruebas unitarias de ThresholdService: orquesta localizar el preview de
/// origen + validación + cache + llamada a Python + storage + versionado +
/// clasificación de advertencia, sin depender de HTTP real ni de OpenCV (eso
/// lo cubren EndToEnd/ThresholdEndpointsTests y los tests de Python). Mismo
/// criterio que PreprocessServiceTests (M1-S03).
/// </summary>
public sealed class ThresholdServiceTests
{
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ImageId = Guid.NewGuid();
    private static readonly Guid SourcePreviewId = Guid.NewGuid();
    private const string SourceStorageKey = "project/image/previews/source.png";

    private static (ThresholdService Service, FakeFileStorage Storage, InMemoryThresholdConfigRegistry ThresholdRegistry, FakePythonThresholdClient PythonClient) CreateService(
        bool withExistingSourcePreview = true, ThresholdOptions? options = null)
    {
        var preprocessService = new FakePreprocessService();
        if (withExistingSourcePreview)
        {
            preprocessService.AddPreview(new PreprocessConfigRecord(
                ProjectId, ImageId, 1, SourcePreviewId,
                new PreprocessParameters(false, 1.0, 0, 0),
                SourceStorageKey, "image/png", 10, 10, 10, 10,
                new PreprocessMetrics(128.0, 10.0, 0, 255),
                DateTimeOffset.UtcNow));
        }

        var storage = new FakeFileStorage();
        storage.Saved[SourceStorageKey] = [9, 9, 9, 9];

        var thresholdRegistry = new InMemoryThresholdConfigRegistry();
        var validator = new ThresholdParameterValidator(Microsoft.Extensions.Options.Options.Create(options ?? new ThresholdOptions()));
        var pythonClient = new FakePythonThresholdClient();

        var service = new ThresholdService(
            preprocessService, thresholdRegistry, validator, pythonClient, storage,
            Microsoft.Extensions.Options.Options.Create(options ?? new ThresholdOptions()), NullLogger<ThresholdService>.Instance);

        return (service, storage, thresholdRegistry, pythonClient);
    }

    private static ThresholdRequest DefaultRequest(int value = 128, bool invert = false, Guid? previewId = null) =>
        new(previewId ?? SourcePreviewId, value, invert);

    [Fact]
    public async Task GenerateMaskAsync_WhenSourcePreviewDoesNotExist_ReturnsNotFound()
    {
        var (service, _, _, _) = CreateService(withExistingSourcePreview: false);

        var result = await service.GenerateMaskAsync(ProjectId, ImageId, DefaultRequest(), CancellationToken.None);

        Assert.IsType<ThresholdResult.NotFound>(result);
    }

    [Fact]
    public async Task GenerateMaskAsync_WhenParametersOutOfRange_ReturnsValidationFailedWithoutCallingPython()
    {
        var (service, _, _, pythonClient) = CreateService();

        var result = await service.GenerateMaskAsync(ProjectId, ImageId, DefaultRequest(value: 999), CancellationToken.None);

        var failed = Assert.IsType<ThresholdResult.ValidationFailed>(result);
        Assert.Equal("invalid_parameters", failed.Code);
        Assert.Equal(0, pythonClient.CallCount);
    }

    [Fact]
    public async Task GenerateMaskAsync_WhenCalledFirstTime_GeneratesNewMaskAndSavesToStorage()
    {
        var (service, storage, _, pythonClient) = CreateService();

        var result = await service.GenerateMaskAsync(ProjectId, ImageId, DefaultRequest(value: 100), CancellationToken.None);

        var ready = Assert.IsType<ThresholdResult.Ready>(result);
        Assert.False(ready.FromCache);
        Assert.Equal(1, ready.Record.Version);
        Assert.Equal(1, pythonClient.CallCount);
        Assert.True(storage.Saved.ContainsKey(ready.Record.MaskStorageKey));
        Assert.NotEqual(Guid.Empty, ready.Record.MaskId);
        Assert.Equal(SourcePreviewId, ready.Record.SourcePreviewId);
    }

    [Fact]
    public async Task GenerateMaskAsync_WhenSameParamsRequestedAgain_ReturnsCachedWithoutCallingPythonAgain()
    {
        var (service, _, _, pythonClient) = CreateService();
        var request = DefaultRequest(value: 150, invert: true);

        var first = await service.GenerateMaskAsync(ProjectId, ImageId, request, CancellationToken.None);
        var second = await service.GenerateMaskAsync(ProjectId, ImageId, request, CancellationToken.None);

        var firstReady = Assert.IsType<ThresholdResult.Ready>(first);
        var secondReady = Assert.IsType<ThresholdResult.Ready>(second);
        Assert.False(firstReady.FromCache);
        Assert.True(secondReady.FromCache);
        Assert.Equal(firstReady.Record.MaskId, secondReady.Record.MaskId);
        Assert.Equal(firstReady.Record.MaskStorageKey, secondReady.Record.MaskStorageKey);
        Assert.Equal(firstReady.Record.Version + 1, secondReady.Record.Version);
        Assert.Equal(1, pythonClient.CallCount);
    }

    [Fact]
    public async Task GenerateMaskAsync_WhenResetMatchesEarlierVersion_CreatesNewVersionReusingSameMaskFile()
    {
        var (service, _, thresholdRegistry, pythonClient) = CreateService();

        var initial = await service.GenerateMaskAsync(ProjectId, ImageId, DefaultRequest(), CancellationToken.None);
        var changed = await service.GenerateMaskAsync(ProjectId, ImageId, DefaultRequest(value: 200), CancellationToken.None);
        var reset = await service.GenerateMaskAsync(ProjectId, ImageId, DefaultRequest(), CancellationToken.None);

        var initialReady = Assert.IsType<ThresholdResult.Ready>(initial);
        var changedReady = Assert.IsType<ThresholdResult.Ready>(changed);
        var resetReady = Assert.IsType<ThresholdResult.Ready>(reset);

        Assert.Equal(1, initialReady.Record.Version);
        Assert.Equal(2, changedReady.Record.Version);
        Assert.True(resetReady.FromCache);
        Assert.Equal(3, resetReady.Record.Version);
        Assert.Equal(initialReady.Record.MaskId, resetReady.Record.MaskId);
        Assert.Equal(2, pythonClient.CallCount);

        var latest = thresholdRegistry.FindLatest(ProjectId, ImageId);
        Assert.NotNull(latest);
        Assert.Equal(3, latest!.Version);
        Assert.Equal(resetReady.Record.MaskId, latest.MaskId);
    }

    [Fact]
    public async Task GenerateMaskAsync_WhenConcurrentRequestsWithSameParams_CallsPythonOnlyOnce()
    {
        var (service, _, thresholdRegistry, pythonClient) = CreateService();
        pythonClient.Delay = TimeSpan.FromMilliseconds(50);
        var request = DefaultRequest(value: 77, invert: true);

        var task1 = service.GenerateMaskAsync(ProjectId, ImageId, request, CancellationToken.None);
        var task2 = service.GenerateMaskAsync(ProjectId, ImageId, request, CancellationToken.None);
        var results = await Task.WhenAll(task1, task2);

        Assert.Equal(1, pythonClient.CallCount);

        var ready1 = Assert.IsType<ThresholdResult.Ready>(results[0]);
        var ready2 = Assert.IsType<ThresholdResult.Ready>(results[1]);
        Assert.Equal(ready1.Record.MaskId, ready2.Record.MaskId);

        var latest = thresholdRegistry.FindLatest(ProjectId, ImageId);
        Assert.NotNull(latest);
        Assert.Equal(ready1.Record.MaskId, latest!.MaskId);
    }

    [Fact]
    public async Task GenerateMaskAsync_WhenSameParamsButDifferentSourcePreview_TreatsAsDifferentMask()
    {
        var otherPreviewId = Guid.NewGuid();
        var preprocessService = new FakePreprocessService();
        preprocessService.AddPreview(new PreprocessConfigRecord(
            ProjectId, ImageId, 1, SourcePreviewId, new PreprocessParameters(false, 1.0, 0, 0),
            SourceStorageKey, "image/png", 10, 10, 10, 10, new PreprocessMetrics(128.0, 10.0, 0, 255), DateTimeOffset.UtcNow));
        preprocessService.AddPreview(new PreprocessConfigRecord(
            ProjectId, ImageId, 2, otherPreviewId, new PreprocessParameters(true, 1.0, 0, 0),
            SourceStorageKey, "image/png", 10, 10, 10, 10, new PreprocessMetrics(128.0, 10.0, 0, 255), DateTimeOffset.UtcNow));

        var storage = new FakeFileStorage();
        storage.Saved[SourceStorageKey] = [9, 9, 9, 9];
        var thresholdRegistry = new InMemoryThresholdConfigRegistry();
        var validator = new ThresholdParameterValidator(Microsoft.Extensions.Options.Options.Create(new ThresholdOptions()));
        var pythonClient = new FakePythonThresholdClient();
        var service = new ThresholdService(
            preprocessService, thresholdRegistry, validator, pythonClient, storage,
            Microsoft.Extensions.Options.Options.Create(new ThresholdOptions()), NullLogger<ThresholdService>.Instance);

        var first = await service.GenerateMaskAsync(ProjectId, ImageId, DefaultRequest(value: 128, previewId: SourcePreviewId), CancellationToken.None);
        var second = await service.GenerateMaskAsync(ProjectId, ImageId, DefaultRequest(value: 128, previewId: otherPreviewId), CancellationToken.None);

        var firstReady = Assert.IsType<ThresholdResult.Ready>(first);
        var secondReady = Assert.IsType<ThresholdResult.Ready>(second);
        Assert.False(secondReady.FromCache); // no cachea entre previews de origen distintos
        Assert.NotEqual(firstReady.Record.MaskId, secondReady.Record.MaskId);
        Assert.Equal(2, pythonClient.CallCount);
    }

    [Fact]
    public async Task GenerateMaskAsync_WhenForegroundPercentIsBelowNearEmptyThreshold_ReturnsNearEmptyWarning()
    {
        var options = new ThresholdOptions { NearEmptyMaxForegroundPercent = 2.0, NearFullMinForegroundPercent = 98.0 };
        var (service, _, _, pythonClient) = CreateService(options: options);
        pythonClient.Respond = parameters => new PythonThresholdResult(
            PythonThresholdState.Success, [1], "image/png", 10, 10, parameters,
            new ThresholdRawMetrics(1.0, 99.0), Message: null);

        var result = await service.GenerateMaskAsync(ProjectId, ImageId, DefaultRequest(), CancellationToken.None);

        var ready = Assert.IsType<ThresholdResult.Ready>(result);
        Assert.True(ready.Record.Metrics.IsNearEmpty);
        Assert.False(ready.Record.Metrics.IsNearFull);
        Assert.Equal("mask_near_empty", ready.Record.Metrics.WarningCode);
        Assert.NotNull(ready.Record.Metrics.WarningMessage);
    }

    [Fact]
    public async Task GenerateMaskAsync_WhenForegroundPercentIsAboveNearFullThreshold_ReturnsNearFullWarning()
    {
        var options = new ThresholdOptions { NearEmptyMaxForegroundPercent = 2.0, NearFullMinForegroundPercent = 98.0 };
        var (service, _, _, pythonClient) = CreateService(options: options);
        pythonClient.Respond = parameters => new PythonThresholdResult(
            PythonThresholdState.Success, [1], "image/png", 10, 10, parameters,
            new ThresholdRawMetrics(99.0, 1.0), Message: null);

        var result = await service.GenerateMaskAsync(ProjectId, ImageId, DefaultRequest(), CancellationToken.None);

        var ready = Assert.IsType<ThresholdResult.Ready>(result);
        Assert.True(ready.Record.Metrics.IsNearFull);
        Assert.False(ready.Record.Metrics.IsNearEmpty);
        Assert.Equal("mask_near_full", ready.Record.Metrics.WarningCode);
    }

    [Fact]
    public async Task GenerateMaskAsync_WhenForegroundPercentIsInReasonableRange_HasNoWarning()
    {
        var (service, _, _, pythonClient) = CreateService();
        pythonClient.Respond = parameters => new PythonThresholdResult(
            PythonThresholdState.Success, [1], "image/png", 10, 10, parameters,
            new ThresholdRawMetrics(45.0, 55.0), Message: null);

        var result = await service.GenerateMaskAsync(ProjectId, ImageId, DefaultRequest(), CancellationToken.None);

        var ready = Assert.IsType<ThresholdResult.Ready>(result);
        Assert.False(ready.Record.Metrics.IsNearEmpty);
        Assert.False(ready.Record.Metrics.IsNearFull);
        Assert.Null(ready.Record.Metrics.WarningCode);
        Assert.Null(ready.Record.Metrics.WarningMessage);
    }

    [Fact]
    public async Task GenerateMaskAsync_WhenPythonReportsCorruptImage_ReturnsUpstreamErrorWithCorruptFileCode()
    {
        var (service, _, _, pythonClient) = CreateService();
        pythonClient.Respond = _ => new PythonThresholdResult(
            PythonThresholdState.CorruptImage, null, null, null, null, null, null, "corrupta");

        var result = await service.GenerateMaskAsync(ProjectId, ImageId, DefaultRequest(), CancellationToken.None);

        var error = Assert.IsType<ThresholdResult.UpstreamError>(result);
        Assert.Equal("corrupt_file", error.Code);
    }

    [Fact]
    public async Task GenerateMaskAsync_WhenPythonIsUnavailable_ReturnsUpstreamErrorWithEngineUnavailableCode()
    {
        var (service, _, _, pythonClient) = CreateService();
        pythonClient.Respond = _ => new PythonThresholdResult(
            PythonThresholdState.Unavailable, null, null, null, null, null, null, "sin conexión");

        var result = await service.GenerateMaskAsync(ProjectId, ImageId, DefaultRequest(), CancellationToken.None);

        var error = Assert.IsType<ThresholdResult.UpstreamError>(result);
        Assert.Equal("engine_unavailable", error.Code);
    }

    [Fact]
    public async Task FindMask_WhenMaskWasGenerated_ReturnsRecord()
    {
        var (service, _, _, _) = CreateService();
        var generated = await service.GenerateMaskAsync(ProjectId, ImageId, DefaultRequest(), CancellationToken.None);
        var ready = Assert.IsType<ThresholdResult.Ready>(generated);

        var found = service.FindMask(ProjectId, ImageId, ready.Record.MaskId);

        Assert.NotNull(found);
        Assert.Equal(ready.Record.MaskId, found!.MaskId);
    }

    [Fact]
    public void FindMask_WhenMaskDoesNotExist_ReturnsNull()
    {
        var (service, _, _, _) = CreateService();

        var found = service.FindMask(ProjectId, ImageId, Guid.NewGuid());

        Assert.Null(found);
    }
}
