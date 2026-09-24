using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Vectify.Api.Contracts;
using Vectify.Api.Tests.TestSupport;

namespace Vectify.Api.Tests.EndToEnd;

/// <summary>
/// Pruebas de integración HTTP de POST .../threshold y GET .../masks/{id}:
/// levantan la Web API real (WebApplicationFactory) con almacenamiento local
/// apuntando a un directorio temporal y un motor Python simulado
/// (FakePythonPreprocessServer, sin OpenCV real -- el pipeline determinista en
/// sí se prueba en services/python-engine/tests). Cubre el contrato, que
/// threshold opera sobre un preview YA preprocesado (nunca el original), el
/// cache por (preview de origen + parámetros), el versionado, la validación
/// de rangos, las advertencias de máscara casi vacía/llena y los errores
/// controlados que puede reportar Python.
/// </summary>
public sealed class ThresholdEndpointsTests : IDisposable
{
    private readonly string _storageRoot = Path.Combine(Path.GetTempPath(), "vectify-threshold-tests-" + Guid.NewGuid().ToString("n"));
    private readonly string _projectRegistryRoot = Path.Combine(Path.GetTempPath(), "vectify-threshold-tests-registry-" + Guid.NewGuid().ToString("n"));

    [Fact]
    public async Task PostThreshold_WhenParamsAreValid_CreatesMaskAndItIsRetrievable()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(
            _ => (200, PreprocessPayloads.SuccessBody()),
            _ => (200, ThresholdPayloads.SuccessBody(value: 140, invert: true, foregroundPercent: 33.3, backgroundPercent: 66.7)));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var (projectId, imageId, previewId) = await CreateProjectWithPreviewAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/threshold",
            new ThresholdRequest(previewId, 140, true));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ThresholdResponse>();
        Assert.NotNull(body);
        Assert.False(body!.Cached);
        Assert.Equal(1, body.Version);
        Assert.Equal(previewId, body.SourcePreviewId);
        Assert.Equal(140, body.EffectiveParams.Value);
        Assert.True(body.EffectiveParams.Invert);
        Assert.NotNull(response.Headers.Location);

        var maskResponse = await client.GetAsync(response.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, maskResponse.StatusCode);
        Assert.NotEmpty(await maskResponse.Content.ReadAsByteArrayAsync());
        Assert.Equal(1, pythonServer.ThresholdRequestCount);
    }

    [Fact]
    public async Task PostThreshold_WhenCalledTwiceWithSameParams_ReturnsCachedWithoutCallingPythonAgain()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(
            _ => (200, PreprocessPayloads.SuccessBody()),
            _ => (200, ThresholdPayloads.SuccessBody(value: 100)));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var (projectId, imageId, previewId) = await CreateProjectWithPreviewAsync(client);
        var request = new ThresholdRequest(previewId, 100, false);

        var first = await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/images/{imageId}/threshold", request);
        var second = await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/images/{imageId}/threshold", request);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var firstBody = await first.Content.ReadFromJsonAsync<ThresholdResponse>();
        var secondBody = await second.Content.ReadFromJsonAsync<ThresholdResponse>();
        Assert.False(firstBody!.Cached);
        Assert.True(secondBody!.Cached);
        Assert.Equal(firstBody.MaskId, secondBody.MaskId);
        Assert.Equal(1, pythonServer.ThresholdRequestCount);
    }

    [Fact]
    public async Task PostThreshold_WhenResetToDefaultsAfterModifying_GeneratesNewVersionReproducibly()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(
            _ => (200, PreprocessPayloads.SuccessBody()),
            count => (200, ThresholdPayloads.SuccessBody(value: count == 1 ? 200 : 128)));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var (projectId, imageId, previewId) = await CreateProjectWithPreviewAsync(client);

        var modified = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/threshold", new ThresholdRequest(previewId, 200, false));
        var reset = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/threshold", new ThresholdRequest(previewId, 128, false));

        var modifiedBody = await modified.Content.ReadFromJsonAsync<ThresholdResponse>();
        var resetBody = await reset.Content.ReadFromJsonAsync<ThresholdResponse>();
        Assert.Equal(1, modifiedBody!.Version);
        Assert.Equal(2, resetBody!.Version);
        Assert.Equal(128, resetBody.EffectiveParams.Value);
    }

    [Fact]
    public async Task PostThreshold_WhenValueIsOutOfRange_ReturnsBadRequestWithInvalidParameters()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(_ => (200, PreprocessPayloads.SuccessBody()));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var (projectId, imageId, previewId) = await CreateProjectWithPreviewAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/threshold", new ThresholdRequest(previewId, 999, false));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.Equal("invalid_parameters", error!.Code);
        Assert.Equal(0, pythonServer.ThresholdRequestCount);
    }

    [Fact]
    public async Task PostThreshold_WhenSourcePreviewDoesNotExist_ReturnsNotFound()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(_ => (200, PreprocessPayloads.SuccessBody()));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var (projectId, imageId, _) = await CreateProjectWithPreviewAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/threshold", new ThresholdRequest(Guid.NewGuid(), 128, false));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostThreshold_WhenImageDoesNotExist_ReturnsNotFound()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(_ => (200, PreprocessPayloads.SuccessBody()));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/projects/{Guid.NewGuid()}/images/{Guid.NewGuid()}/threshold",
            new ThresholdRequest(Guid.NewGuid(), 128, false));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostThreshold_WhenMaskIsNearEmpty_ReturnsWarningCode()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(
            _ => (200, PreprocessPayloads.SuccessBody()),
            _ => (200, ThresholdPayloads.SuccessBody(foregroundPercent: 0.5, backgroundPercent: 99.5)));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var (projectId, imageId, previewId) = await CreateProjectWithPreviewAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/threshold", new ThresholdRequest(previewId, 250, false));

        var body = await response.Content.ReadFromJsonAsync<ThresholdResponse>();
        Assert.True(body!.Metrics.IsNearEmpty);
        Assert.Equal("mask_near_empty", body.Metrics.WarningCode);
        Assert.NotNull(body.Metrics.WarningMessage);
    }

    [Fact]
    public async Task PostThreshold_WhenMaskIsNearFull_ReturnsWarningCode()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(
            _ => (200, PreprocessPayloads.SuccessBody()),
            _ => (200, ThresholdPayloads.SuccessBody(foregroundPercent: 99.7, backgroundPercent: 0.3)));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var (projectId, imageId, previewId) = await CreateProjectWithPreviewAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/threshold", new ThresholdRequest(previewId, 5, false));

        var body = await response.Content.ReadFromJsonAsync<ThresholdResponse>();
        Assert.True(body!.Metrics.IsNearFull);
        Assert.Equal("mask_near_full", body.Metrics.WarningCode);
    }

    [Fact]
    public async Task PostThreshold_WhenPythonReportsDimensionsExceeded_ReturnsPayloadTooLarge()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(
            _ => (200, PreprocessPayloads.SuccessBody()),
            _ => (413, ThresholdPayloads.ErrorBody("dimensions_exceeded", "demasiado grande")));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var (projectId, imageId, previewId) = await CreateProjectWithPreviewAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/threshold", new ThresholdRequest(previewId, 128, false));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.Equal("dimensions_exceeded", error!.Code);
    }

    [Fact]
    public async Task GetThresholdMaskImage_WhenMaskDoesNotExist_ReturnsNotFound()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(_ => (200, PreprocessPayloads.SuccessBody()));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/v1/projects/{Guid.NewGuid()}/images/{Guid.NewGuid()}/masks/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<(Guid ProjectId, Guid ImageId, Guid PreviewId)> CreateProjectWithPreviewAsync(HttpClient client)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(SampleImages.ValidPng1x1);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", "logo.png");

        var uploadResponse = await client.PostAsync("/api/v1/projects", content);
        uploadResponse.EnsureSuccessStatusCode();
        var uploadBody = await uploadResponse.Content.ReadFromJsonAsync<UploadImageResponse>();

        var previewResponse = await client.PostAsJsonAsync(
            $"/api/v1/projects/{uploadBody!.ProjectId}/images/{uploadBody.ImageId}/preview",
            new PreprocessRequest(false, 1.0, 0, 0));
        previewResponse.EnsureSuccessStatusCode();
        var previewBody = await previewResponse.Content.ReadFromJsonAsync<PreprocessResponse>();

        return (uploadBody.ProjectId, uploadBody.ImageId, previewBody!.PreviewId);
    }

    private WebApplicationFactory<Program> CreateFactory(string pythonBaseUrl)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["PythonEngine:BaseUrl"] = pythonBaseUrl,
                    ["Cors:AllowedOrigins"] = "http://localhost:5173",
                    ["Storage:RootPath"] = _storageRoot,
                    ["ProjectRegistry:RootPath"] = _projectRegistryRoot,
                });
            });
        });
    }

    public void Dispose()
    {
        if (Directory.Exists(_storageRoot))
        {
            Directory.Delete(_storageRoot, recursive: true);
        }

        if (Directory.Exists(_projectRegistryRoot))
        {
            Directory.Delete(_projectRegistryRoot, recursive: true);
        }
    }
}
