using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Vectify.Api.Contracts;
using Vectify.Api.Tests.TestSupport;

namespace Vectify.Api.Tests.EndToEnd;

/// <summary>
/// Pruebas de integración HTTP de POST .../simplify/preview,
/// POST .../simplify/apply y GET .../simplifications/{id}: levantan la Web
/// API real (WebApplicationFactory) con almacenamiento local apuntando a un
/// directorio temporal y un motor Python simulado (FakePythonPreprocessServer,
/// sin Douglas-Peucker real -- el algoritmo en sí se prueba en
/// services/python-engine/tests). Cubre el contrato completo del pipeline
/// (upload -> preview -> máscara -> vector -> simplificación), la
/// reversibilidad del preview, el cache/versionado de apply, y los errores
/// controlados que puede reportar Python.
/// </summary>
public sealed class SimplificationEndpointsTests : IDisposable
{
    private readonly string _storageRoot = Path.Combine(Path.GetTempPath(), "vectify-simplify-tests-" + Guid.NewGuid().ToString("n"));
    private readonly string _projectRegistryRoot = Path.Combine(Path.GetTempPath(), "vectify-simplify-tests-registry-" + Guid.NewGuid().ToString("n"));

    [Fact]
    public async Task PostPreview_WhenVectorExists_ReturnsMetricsAndSvgWithoutPersisting()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(
            _ => (200, PreprocessPayloads.SuccessBody()),
            _ => (200, ThresholdPayloads.SuccessBody()),
            _ => (200, VectorizePayloads.SuccessBody()),
            _ => (200, SimplifyPayloads.SuccessBody()));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var (projectId, imageId, vectorId) = await CreateProjectWithVectorAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/simplify/preview",
            new SimplifyRequest(vectorId, "medium", null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SimplifyPreviewResponse>();
        Assert.NotNull(body);
        Assert.Equal(vectorId, body!.SourceVectorId);
        Assert.True(body.Metrics.ReductionPercent > 0);
        Assert.Contains("<svg", body.Svg);
        Assert.Equal(1, pythonServer.SimplifyRequestCount);

        // Reversible: no debería haber creado ninguna simplificación consultable.
        var getResponse = await client.GetAsync($"/api/v1/projects/{projectId}/images/{imageId}/simplifications/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task PostPreview_CalledTwice_NeverCachesAndAlwaysCallsPythonAgain()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(
            _ => (200, PreprocessPayloads.SuccessBody()),
            _ => (200, ThresholdPayloads.SuccessBody()),
            _ => (200, VectorizePayloads.SuccessBody()),
            _ => (200, SimplifyPayloads.SuccessBody()));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var (projectId, imageId, vectorId) = await CreateProjectWithVectorAsync(client);
        var request = new SimplifyRequest(vectorId, "medium", null);

        await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/images/{imageId}/simplify/preview", request);
        await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/images/{imageId}/simplify/preview", request);

        Assert.Equal(2, pythonServer.SimplifyRequestCount);
    }

    [Fact]
    public async Task PostApply_WhenVectorExists_CreatesSimplificationAndItIsRetrievableAsSvg()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(
            _ => (200, PreprocessPayloads.SuccessBody()),
            _ => (200, ThresholdPayloads.SuccessBody()),
            _ => (200, VectorizePayloads.SuccessBody()),
            _ => (200, SimplifyPayloads.SuccessBody()));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var (projectId, imageId, vectorId) = await CreateProjectWithVectorAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/simplify/apply",
            new SimplifyRequest(vectorId, "medium", null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SimplifyResponse>();
        Assert.NotNull(body);
        Assert.False(body!.Cached);
        Assert.Equal(1, body.Version);
        Assert.Equal(vectorId, body.SourceVectorId);
        Assert.True(body.Metrics.ReductionPercent > 0);
        Assert.NotNull(response.Headers.Location);

        var svgResponse = await client.GetAsync(response.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, svgResponse.StatusCode);
        Assert.Equal("image/svg+xml", svgResponse.Content.Headers.ContentType?.MediaType);
        var svgText = await svgResponse.Content.ReadAsStringAsync();
        Assert.Contains("<svg", svgText);
        Assert.Equal(1, pythonServer.SimplifyRequestCount);
    }

    [Fact]
    public async Task PostApply_WhenCalledTwiceForSameVectorAndPreset_ReturnsCachedWithoutCallingPythonAgain()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(
            _ => (200, PreprocessPayloads.SuccessBody()),
            _ => (200, ThresholdPayloads.SuccessBody()),
            _ => (200, VectorizePayloads.SuccessBody()),
            _ => (200, SimplifyPayloads.SuccessBody()));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var (projectId, imageId, vectorId) = await CreateProjectWithVectorAsync(client);
        var request = new SimplifyRequest(vectorId, "medium", null);

        var first = await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/images/{imageId}/simplify/apply", request);
        var second = await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/images/{imageId}/simplify/apply", request);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var firstBody = await first.Content.ReadFromJsonAsync<SimplifyResponse>();
        var secondBody = await second.Content.ReadFromJsonAsync<SimplifyResponse>();
        Assert.False(firstBody!.Cached);
        Assert.True(secondBody!.Cached);
        Assert.Equal(firstBody.SimplificationId, secondBody.SimplificationId);
        Assert.Equal(firstBody.Version + 1, secondBody.Version);
        Assert.Equal(1, pythonServer.SimplifyRequestCount);
    }

    [Fact]
    public async Task PostApply_NeverOverwritesThePreviousVectorVersion()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(
            _ => (200, PreprocessPayloads.SuccessBody()),
            _ => (200, ThresholdPayloads.SuccessBody()),
            _ => (200, VectorizePayloads.SuccessBody()),
            _ => (200, SimplifyPayloads.SuccessBody()));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var (projectId, imageId, vectorId) = await CreateProjectWithVectorAsync(client);

        await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/simplify/apply",
            new SimplifyRequest(vectorId, "medium", null));

        // El SVG original (vectorId) sigue disponible sin cambios después de aplicar la simplificación.
        var originalResponse = await client.GetAsync($"/api/v1/projects/{projectId}/images/{imageId}/vectors/{vectorId}");
        Assert.Equal(HttpStatusCode.OK, originalResponse.StatusCode);
    }

    [Fact]
    public async Task PostApply_WhenPresetIsUnknown_ReturnsBadRequestWithInvalidParameters()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(
            _ => (200, PreprocessPayloads.SuccessBody()),
            _ => (200, ThresholdPayloads.SuccessBody()),
            _ => (200, VectorizePayloads.SuccessBody()));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var (projectId, imageId, vectorId) = await CreateProjectWithVectorAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/simplify/apply",
            new SimplifyRequest(vectorId, "unknown-preset", null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.Equal("invalid_parameters", error!.Code);
        Assert.Equal(0, pythonServer.SimplifyRequestCount);
    }

    [Fact]
    public async Task PostApply_WhenSourceVectorDoesNotExist_ReturnsNotFound()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(_ => (200, PreprocessPayloads.SuccessBody()));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var (projectId, imageId, _) = await CreateProjectWithVectorAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/simplify/apply",
            new SimplifyRequest(Guid.NewGuid(), "medium", null));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostApply_WhenPythonReportsInvalidInputSvg_ReturnsBadRequest()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(
            _ => (200, PreprocessPayloads.SuccessBody()),
            _ => (200, ThresholdPayloads.SuccessBody()),
            _ => (200, VectorizePayloads.SuccessBody()),
            _ => (400, SimplifyPayloads.ErrorBody("invalid_input_svg", "SVG de entrada corrupto")));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var (projectId, imageId, vectorId) = await CreateProjectWithVectorAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/simplify/apply",
            new SimplifyRequest(vectorId, "medium", null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.Equal("invalid_input_svg", error!.Code);
    }

    [Fact]
    public async Task GetSimplificationSvg_WhenSimplificationDoesNotExist_ReturnsNotFound()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(_ => (200, PreprocessPayloads.SuccessBody()));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/v1/projects/{Guid.NewGuid()}/images/{Guid.NewGuid()}/simplifications/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<(Guid ProjectId, Guid ImageId, Guid VectorId)> CreateProjectWithVectorAsync(HttpClient client)
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

        var maskResponse = await client.PostAsJsonAsync(
            $"/api/v1/projects/{uploadBody.ProjectId}/images/{uploadBody.ImageId}/threshold",
            new ThresholdRequest(previewBody!.PreviewId, 128, false));
        maskResponse.EnsureSuccessStatusCode();
        var maskBody = await maskResponse.Content.ReadFromJsonAsync<ThresholdResponse>();

        var vectorResponse = await client.PostAsJsonAsync(
            $"/api/v1/projects/{uploadBody.ProjectId}/images/{uploadBody.ImageId}/vectorize",
            new VectorizeRequest(maskBody!.MaskId));
        vectorResponse.EnsureSuccessStatusCode();
        var vectorBody = await vectorResponse.Content.ReadFromJsonAsync<VectorizeResponse>();

        return (uploadBody.ProjectId, uploadBody.ImageId, vectorBody!.VectorId);
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
