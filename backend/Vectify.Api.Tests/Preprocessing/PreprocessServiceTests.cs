using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vectify.Api.Clients;
using Vectify.Api.Contracts;
using Vectify.Api.Options;
using Vectify.Api.Preprocessing;
using Vectify.Api.Projects;
using Vectify.Api.Tests.Projects;

namespace Vectify.Api.Tests.Preprocessing;

/// <summary>
/// Pruebas unitarias de PreprocessService: orquesta validación + cache +
/// llamada a Python + storage + versionado sin depender de HTTP real ni de
/// OpenCV (eso lo cubren EndToEnd/PreprocessEndpointsTests y los tests de
/// Python). Cubre el camino feliz, el cache por parámetros, el versionado y
/// los errores controlados que devuelve Python.
/// </summary>
public sealed class PreprocessServiceTests
{
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ImageId = Guid.NewGuid();
    private const string StorageKey = "project/image/original.png";

    private static (PreprocessService Service, FakeFileStorage Storage, InMemoryPreprocessConfigRegistry PreprocessRegistry, FakePythonPreprocessClient PythonClient) CreateService(
        bool withExistingImage = true)
    {
        var projectRegistry = new InMemoryProjectRegistry();
        if (withExistingImage)
        {
            projectRegistry.Save(new ProjectRecord(
                ProjectId, ImageId, "logo.png", "image/png", 4, 10, 10, "uploaded", StorageKey, null, DateTimeOffset.UtcNow));
        }

        var storage = new FakeFileStorage();
        storage.Saved[StorageKey] = [9, 9, 9, 9];

        var preprocessRegistry = new InMemoryPreprocessConfigRegistry();
        var validator = new PreprocessParameterValidator(Microsoft.Extensions.Options.Options.Create(new PreprocessOptions()));
        var pythonClient = new FakePythonPreprocessClient();

        var service = new PreprocessService(
            projectRegistry, preprocessRegistry, validator, pythonClient, storage, NullLogger<PreprocessService>.Instance);

        return (service, storage, preprocessRegistry, pythonClient);
    }

    private static PreprocessRequest DefaultRequest(bool grayscale = false, double contrast = 1.0, int brightness = 0, int denoise = 0) =>
        new(grayscale, contrast, brightness, denoise);

    [Fact]
    public async Task GeneratePreviewAsync_WhenImageDoesNotExist_ReturnsNotFound()
    {
        var (service, _, _, _) = CreateService(withExistingImage: false);

        var result = await service.GeneratePreviewAsync(ProjectId, ImageId, DefaultRequest(), CancellationToken.None);

        Assert.IsType<PreprocessResult.NotFound>(result);
    }

    [Fact]
    public async Task GeneratePreviewAsync_WhenParametersOutOfRange_ReturnsValidationFailedWithoutCallingPython()
    {
        var (service, _, _, pythonClient) = CreateService();

        var result = await service.GeneratePreviewAsync(ProjectId, ImageId, DefaultRequest(contrast: 5.0), CancellationToken.None);

        var failed = Assert.IsType<PreprocessResult.ValidationFailed>(result);
        Assert.Equal("invalid_parameters", failed.Code);
        Assert.Equal(0, pythonClient.CallCount);
    }

    [Fact]
    public async Task GeneratePreviewAsync_WhenCalledFirstTime_GeneratesNewPreviewAndSavesToStorage()
    {
        var (service, storage, _, pythonClient) = CreateService();

        var result = await service.GeneratePreviewAsync(ProjectId, ImageId, DefaultRequest(grayscale: true), CancellationToken.None);

        var ready = Assert.IsType<PreprocessResult.Ready>(result);
        Assert.False(ready.FromCache);
        Assert.Equal(1, ready.Record.Version);
        Assert.Equal(1, pythonClient.CallCount);
        Assert.True(storage.Saved.ContainsKey(ready.Record.PreviewStorageKey));
        Assert.NotEqual(Guid.Empty, ready.Record.PreviewId);
    }

    [Fact]
    public async Task GeneratePreviewAsync_WhenSameParamsRequestedAgain_ReturnsCachedWithoutCallingPythonAgain()
    {
        var (service, _, _, pythonClient) = CreateService();
        var request = DefaultRequest(contrast: 1.5, brightness: 20);

        var first = await service.GeneratePreviewAsync(ProjectId, ImageId, request, CancellationToken.None);
        var second = await service.GeneratePreviewAsync(ProjectId, ImageId, request, CancellationToken.None);

        var firstReady = Assert.IsType<PreprocessResult.Ready>(first);
        var secondReady = Assert.IsType<PreprocessResult.Ready>(second);
        Assert.False(firstReady.FromCache);
        Assert.True(secondReady.FromCache);
        // El cache-hit reutiliza el mismo archivo/preview generado...
        Assert.Equal(firstReady.Record.PreviewId, secondReady.Record.PreviewId);
        Assert.Equal(firstReady.Record.PreviewStorageKey, secondReady.Record.PreviewStorageKey);
        // ...pero SÍ avanza la versión del historial (defecto corregido: antes
        // un cache-hit devolvía la versión vieja y no actualizaba _latest).
        Assert.Equal(firstReady.Record.Version + 1, secondReady.Record.Version);
        Assert.Equal(1, pythonClient.CallCount); // no se volvió a llamar a Python
    }

    [Fact]
    public async Task GeneratePreviewAsync_WhenResetMatchesEarlierVersion_CreatesNewVersionReusingSamePreviewFile()
    {
        // Escenario del defecto: preview inicial (v1, params por defecto) ->
        // cambio de parámetros (v2) -> reset a los valores por defecto ->
        // debe ser v3 mostrando los valores por defecto, reutilizando el
        // archivo de v1, y _latest debe reflejar v3 (no quedarse en v2).
        var (service, storage, preprocessRegistry, pythonClient) = CreateService();

        var initial = await service.GeneratePreviewAsync(ProjectId, ImageId, DefaultRequest(), CancellationToken.None);
        var changed = await service.GeneratePreviewAsync(ProjectId, ImageId, DefaultRequest(brightness: 30), CancellationToken.None);
        var reset = await service.GeneratePreviewAsync(ProjectId, ImageId, DefaultRequest(), CancellationToken.None);

        var initialReady = Assert.IsType<PreprocessResult.Ready>(initial);
        var changedReady = Assert.IsType<PreprocessResult.Ready>(changed);
        var resetReady = Assert.IsType<PreprocessResult.Ready>(reset);

        Assert.Equal(1, initialReady.Record.Version);
        Assert.Equal(2, changedReady.Record.Version);
        Assert.True(resetReady.FromCache);
        Assert.Equal(3, resetReady.Record.Version);

        // Mismo archivo/preview que la generación original (no se regeneró).
        Assert.Equal(initialReady.Record.PreviewId, resetReady.Record.PreviewId);
        Assert.Equal(initialReady.Record.PreviewStorageKey, resetReady.Record.PreviewStorageKey);
        Assert.Equal(2, pythonClient.CallCount); // solo 2 llamadas reales a Python (v1 y v2)

        // _latest queda en v3, no en v2.
        var latest = preprocessRegistry.FindLatest(ProjectId, ImageId);
        Assert.NotNull(latest);
        Assert.Equal(3, latest!.Version);
        Assert.Equal(resetReady.Record.PreviewId, latest.PreviewId);
    }

    [Fact]
    public async Task GeneratePreviewAsync_WhenConcurrentRequestsWithSameParams_CallsPythonOnlyOnce()
    {
        var (service, _, preprocessRegistry, pythonClient) = CreateService();
        pythonClient.Delay = TimeSpan.FromMilliseconds(50);
        var request = DefaultRequest(contrast: 1.2, brightness: 5);

        var task1 = service.GeneratePreviewAsync(ProjectId, ImageId, request, CancellationToken.None);
        var task2 = service.GeneratePreviewAsync(ProjectId, ImageId, request, CancellationToken.None);
        var results = await Task.WhenAll(task1, task2);

        Assert.Equal(1, pythonClient.CallCount);

        var ready1 = Assert.IsType<PreprocessResult.Ready>(results[0]);
        var ready2 = Assert.IsType<PreprocessResult.Ready>(results[1]);
        Assert.Equal(ready1.Record.PreviewId, ready2.Record.PreviewId);

        var latest = preprocessRegistry.FindLatest(ProjectId, ImageId);
        Assert.NotNull(latest);
        Assert.Equal(ready1.Record.PreviewId, latest!.PreviewId);
    }

    [Fact]
    public async Task GeneratePreviewAsync_WhenParamsDifferFromPrevious_CreatesNewVersion()
    {
        var (service, _, _, pythonClient) = CreateService();

        var first = await service.GeneratePreviewAsync(ProjectId, ImageId, DefaultRequest(brightness: 10), CancellationToken.None);
        var second = await service.GeneratePreviewAsync(ProjectId, ImageId, DefaultRequest(brightness: 20), CancellationToken.None);

        var firstReady = Assert.IsType<PreprocessResult.Ready>(first);
        var secondReady = Assert.IsType<PreprocessResult.Ready>(second);
        Assert.False(secondReady.FromCache);
        Assert.Equal(1, firstReady.Record.Version);
        Assert.Equal(2, secondReady.Record.Version);
        Assert.NotEqual(firstReady.Record.PreviewId, secondReady.Record.PreviewId);
        Assert.Equal(2, pythonClient.CallCount);
    }

    [Fact]
    public async Task GeneratePreviewAsync_WhenResetToDefaults_IsJustAnotherParameterCombination()
    {
        var (service, _, _, _) = CreateService();

        var modified = await service.GeneratePreviewAsync(ProjectId, ImageId, DefaultRequest(contrast: 2.0), CancellationToken.None);
        var reset = await service.GeneratePreviewAsync(ProjectId, ImageId, DefaultRequest(), CancellationToken.None);

        var modifiedReady = Assert.IsType<PreprocessResult.Ready>(modified);
        var resetReady = Assert.IsType<PreprocessResult.Ready>(reset);
        Assert.Equal(new PreprocessParameters(false, 1.0, 0, 0), resetReady.Record.Parameters);
        Assert.NotEqual(modifiedReady.Record.PreviewId, resetReady.Record.PreviewId);
    }

    [Fact]
    public async Task GeneratePreviewAsync_WhenPythonReportsCorruptImage_ReturnsUpstreamErrorWithCorruptFileCode()
    {
        var (service, _, _, pythonClient) = CreateService();
        pythonClient.Respond = _ => new PythonPreprocessResult(
            PythonPreprocessState.CorruptImage, null, null, null, null, null, null, null, null, "corrupta");

        var result = await service.GeneratePreviewAsync(ProjectId, ImageId, DefaultRequest(), CancellationToken.None);

        var error = Assert.IsType<PreprocessResult.UpstreamError>(result);
        Assert.Equal("corrupt_file", error.Code);
    }

    [Fact]
    public async Task GeneratePreviewAsync_WhenPythonReportsDimensionsExceeded_ReturnsUpstreamErrorWithDimensionsExceededCode()
    {
        var (service, _, _, pythonClient) = CreateService();
        pythonClient.Respond = _ => new PythonPreprocessResult(
            PythonPreprocessState.DimensionsExceeded, null, null, null, null, null, null, null, null, "muy grande");

        var result = await service.GeneratePreviewAsync(ProjectId, ImageId, DefaultRequest(), CancellationToken.None);

        var error = Assert.IsType<PreprocessResult.UpstreamError>(result);
        Assert.Equal("dimensions_exceeded", error.Code);
    }

    [Fact]
    public async Task GeneratePreviewAsync_WhenPythonIsUnavailable_ReturnsUpstreamErrorWithEngineUnavailableCode()
    {
        var (service, _, _, pythonClient) = CreateService();
        pythonClient.Respond = _ => new PythonPreprocessResult(
            PythonPreprocessState.Unavailable, null, null, null, null, null, null, null, null, "sin conexión");

        var result = await service.GeneratePreviewAsync(ProjectId, ImageId, DefaultRequest(), CancellationToken.None);

        var error = Assert.IsType<PreprocessResult.UpstreamError>(result);
        Assert.Equal("engine_unavailable", error.Code);
    }

    [Fact]
    public async Task FindPreview_WhenPreviewWasGenerated_ReturnsRecord()
    {
        var (service, _, _, _) = CreateService();
        var generated = await service.GeneratePreviewAsync(ProjectId, ImageId, DefaultRequest(), CancellationToken.None);
        var ready = Assert.IsType<PreprocessResult.Ready>(generated);

        var found = service.FindPreview(ProjectId, ImageId, ready.Record.PreviewId);

        Assert.NotNull(found);
        Assert.Equal(ready.Record.PreviewId, found!.PreviewId);
    }

    [Fact]
    public void FindPreview_WhenPreviewDoesNotExist_ReturnsNull()
    {
        var (service, _, _, _) = CreateService();

        var found = service.FindPreview(ProjectId, ImageId, Guid.NewGuid());

        Assert.Null(found);
    }
}
