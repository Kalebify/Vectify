using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Vectify.Api.Contracts;
using Vectify.Api.Tests.TestSupport;

namespace Vectify.Api.Tests.EndToEnd;

/// <summary>
/// Pruebas de integración HTTP de POST .../preview y GET .../previews/{id}:
/// levantan la Web API real (WebApplicationFactory) con almacenamiento local
/// apuntando a un directorio temporal y un motor Python simulado
/// (FakePythonPreprocessServer, sin OpenCV real -- el pipeline determinista en
/// sí se prueba en services/python-engine/tests). Cubre el contrato, el cache
/// por parámetros, la validación de rangos y los errores controlados que
/// puede reportar Python.
/// </summary>
public sealed class PreprocessEndpointsTests : IDisposable
{
    private readonly string _storageRoot = Path.Combine(Path.GetTempPath(), "vectify-preprocess-tests-" + Guid.NewGuid().ToString("n"));
    private readonly string _projectRegistryRoot = Path.Combine(Path.GetTempPath(), "vectify-preprocess-tests-registry-" + Guid.NewGuid().ToString("n"));

    [Fact]
    public async Task PostPreview_WhenParamsAreValid_CreatesPreviewAndItIsRetrievable()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(
            _ => (200, PreprocessPayloads.SuccessBody(grayscale: true, contrast: 1.4, brightness: 5, denoise: 2, width: 1, height: 1)));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var (projectId, imageId) = await CreateProjectAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/preview",
            new PreprocessRequest(true, 1.4, 5, 2));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PreprocessResponse>();
        Assert.NotNull(body);
        Assert.False(body!.Cached);
        Assert.Equal(1, body.Version);
        Assert.True(body.EffectiveParams.Grayscale);
        Assert.Equal(1.4, body.EffectiveParams.Contrast);
        Assert.NotNull(response.Headers.Location);

        var previewResponse = await client.GetAsync(response.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        Assert.NotEmpty(await previewResponse.Content.ReadAsByteArrayAsync());
        Assert.Equal(1, pythonServer.RequestCount);
    }

    [Fact]
    public async Task PostPreview_WhenCalledTwiceWithSameParams_ReturnsCachedWithoutCallingPythonAgain()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(
            _ => (200, PreprocessPayloads.SuccessBody(contrast: 1.2, brightness: 8)));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var (projectId, imageId) = await CreateProjectAsync(client);
        var request = new PreprocessRequest(false, 1.2, 8, 0);

        var first = await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/images/{imageId}/preview", request);
        var second = await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/images/{imageId}/preview", request);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var firstBody = await first.Content.ReadFromJsonAsync<PreprocessResponse>();
        var secondBody = await second.Content.ReadFromJsonAsync<PreprocessResponse>();
        Assert.False(firstBody!.Cached);
        Assert.True(secondBody!.Cached);
        Assert.Equal(firstBody.PreviewId, secondBody.PreviewId);
        Assert.Equal(1, pythonServer.RequestCount);
    }

    [Fact]
    public async Task PostPreview_WhenResetToDefaultsAfterModifying_GeneratesNewVersionReproducibly()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(
            count => (200, PreprocessPayloads.SuccessBody(contrast: count == 1 ? 2.0 : 1.0)));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var (projectId, imageId) = await CreateProjectAsync(client);

        var modified = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/preview", new PreprocessRequest(false, 2.0, 0, 0));
        var reset = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/preview", new PreprocessRequest(false, 1.0, 0, 0));

        var modifiedBody = await modified.Content.ReadFromJsonAsync<PreprocessResponse>();
        var resetBody = await reset.Content.ReadFromJsonAsync<PreprocessResponse>();
        Assert.Equal(1, modifiedBody!.Version);
        Assert.Equal(2, resetBody!.Version);
        Assert.Equal(1.0, resetBody.EffectiveParams.Contrast);
    }

    [Fact]
    public async Task PostPreview_WhenParametersAreOutOfRange_ReturnsBadRequestWithInvalidParameters()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(_ => (200, PreprocessPayloads.SuccessBody()));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var (projectId, imageId) = await CreateProjectAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/preview", new PreprocessRequest(false, 10.0, 0, 0));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.Equal("invalid_parameters", error!.Code);
        Assert.Equal(0, pythonServer.RequestCount);
    }

    [Fact]
    public async Task PostPreview_WhenImageDoesNotExist_ReturnsNotFound()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(_ => (200, PreprocessPayloads.SuccessBody()));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/projects/{Guid.NewGuid()}/images/{Guid.NewGuid()}/preview", new PreprocessRequest(false, 1.0, 0, 0));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostPreview_WhenPythonReportsDimensionsExceeded_ReturnsPayloadTooLarge()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(
            _ => (413, PreprocessPayloads.ErrorBody("dimensions_exceeded", "demasiado grande")));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var (projectId, imageId) = await CreateProjectAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/preview", new PreprocessRequest(false, 1.0, 0, 0));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.Equal("dimensions_exceeded", error!.Code);
    }

    [Fact]
    public async Task GetPreviewImage_WhenPreviewDoesNotExist_ReturnsNotFound()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(_ => (200, PreprocessPayloads.SuccessBody()));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/v1/projects/{Guid.NewGuid()}/images/{Guid.NewGuid()}/previews/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<(Guid ProjectId, Guid ImageId)> CreateProjectAsync(HttpClient client)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(SampleImages.ValidPng1x1);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", "logo.png");

        var response = await client.PostAsync("/api/v1/projects", content);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<UploadImageResponse>();
        return (body!.ProjectId, body.ImageId);
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
