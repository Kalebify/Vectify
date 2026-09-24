using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Vectify.Api.Contracts;
using Vectify.Api.Tests.TestSupport;

namespace Vectify.Api.Tests.EndToEnd;

/// <summary>
/// Pruebas de integración HTTP de POST .../vectorize y GET .../vectors/{id}:
/// levantan la Web API real (WebApplicationFactory) con almacenamiento local
/// apuntando a un directorio temporal y un motor Python simulado
/// (FakePythonPreprocessServer, sin VTracer real -- el motor en sí se prueba
/// en services/python-engine/tests). Cubre el contrato completo del
/// pipeline (upload -> preview -> máscara -> vector), el cache por máscara de
/// origen, el versionado, la idempotencia de reintentos y los errores
/// controlados que puede reportar Python.
/// </summary>
public sealed class VectorizationEndpointsTests : IDisposable
{
    private readonly string _storageRoot = Path.Combine(Path.GetTempPath(), "vectify-vectorize-tests-" + Guid.NewGuid().ToString("n"));
    private readonly string _projectRegistryRoot = Path.Combine(Path.GetTempPath(), "vectify-vectorize-tests-registry-" + Guid.NewGuid().ToString("n"));

    [Fact]
    public async Task PostVectorize_WhenMaskExists_CreatesVectorAndItIsRetrievableAsSvg()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(
            _ => (200, PreprocessPayloads.SuccessBody()),
            _ => (200, ThresholdPayloads.SuccessBody()),
            _ => (200, VectorizePayloads.SuccessBody()));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var (projectId, imageId, maskId) = await CreateProjectWithMaskAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/vectorize", new VectorizeRequest(maskId));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<VectorizeResponse>();
        Assert.NotNull(body);
        Assert.False(body!.Cached);
        Assert.Equal(1, body.Version);
        Assert.Equal(maskId, body.SourceMaskId);
        Assert.Equal(1, body.Metrics.PathCount);
        Assert.NotNull(response.Headers.Location);

        var svgResponse = await client.GetAsync(response.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, svgResponse.StatusCode);
        Assert.Equal("image/svg+xml", svgResponse.Content.Headers.ContentType?.MediaType);
        var svgText = await svgResponse.Content.ReadAsStringAsync();
        Assert.Contains("<svg", svgText);
        Assert.Contains("<path", svgText);
        Assert.Equal(1, pythonServer.VectorizeRequestCount);
    }

    [Fact]
    public async Task PostVectorize_WhenCalledTwiceForSameMask_ReturnsCachedWithoutCallingPythonAgain()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(
            _ => (200, PreprocessPayloads.SuccessBody()),
            _ => (200, ThresholdPayloads.SuccessBody()),
            _ => (200, VectorizePayloads.SuccessBody()));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var (projectId, imageId, maskId) = await CreateProjectWithMaskAsync(client);
        var request = new VectorizeRequest(maskId);

        var first = await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/images/{imageId}/vectorize", request);
        var second = await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/images/{imageId}/vectorize", request);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var firstBody = await first.Content.ReadFromJsonAsync<VectorizeResponse>();
        var secondBody = await second.Content.ReadFromJsonAsync<VectorizeResponse>();
        Assert.False(firstBody!.Cached);
        Assert.True(secondBody!.Cached);
        Assert.Equal(firstBody.VectorId, secondBody.VectorId);
        Assert.Equal(firstBody.Version + 1, secondBody.Version);
        Assert.Equal(1, pythonServer.VectorizeRequestCount);
    }

    [Fact]
    public async Task PostVectorize_WhenRetriedThreeTimes_DoesNotDuplicateVectorization()
    {
        // Idempotencia razonable de reintentos: repetir la misma solicitud no
        // debería producir vectorizaciones nuevas ni llamadas nuevas a Python.
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(
            _ => (200, PreprocessPayloads.SuccessBody()),
            _ => (200, ThresholdPayloads.SuccessBody()),
            _ => (200, VectorizePayloads.SuccessBody()));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var (projectId, imageId, maskId) = await CreateProjectWithMaskAsync(client);
        var request = new VectorizeRequest(maskId);

        await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/images/{imageId}/vectorize", request);
        await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/images/{imageId}/vectorize", request);
        var third = await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/images/{imageId}/vectorize", request);

        var thirdBody = await third.Content.ReadFromJsonAsync<VectorizeResponse>();
        Assert.Equal(3, thirdBody!.Version);
        Assert.Equal(1, pythonServer.VectorizeRequestCount);
    }

    [Fact]
    public async Task PostVectorize_WhenMaskIdIsEmpty_ReturnsBadRequestWithInvalidParameters()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(
            _ => (200, PreprocessPayloads.SuccessBody()));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var (projectId, imageId, _) = await CreateProjectWithMaskAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/vectorize", new VectorizeRequest(Guid.Empty));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.Equal("invalid_parameters", error!.Code);
        Assert.Equal(0, pythonServer.VectorizeRequestCount);
    }

    [Fact]
    public async Task PostVectorize_WhenSourceMaskDoesNotExist_ReturnsNotFound()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(
            _ => (200, PreprocessPayloads.SuccessBody()));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var (projectId, imageId, _) = await CreateProjectWithMaskAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/vectorize", new VectorizeRequest(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostVectorize_WhenPythonReportsEmptyMask_ReturnsUnprocessableEntity()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(
            _ => (200, PreprocessPayloads.SuccessBody()),
            _ => (200, ThresholdPayloads.SuccessBody()),
            _ => (422, VectorizePayloads.ErrorBody("empty_mask", "la máscara no tiene foreground")));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var (projectId, imageId, maskId) = await CreateProjectWithMaskAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/vectorize", new VectorizeRequest(maskId));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.Equal("empty_mask", error!.Code);
    }

    [Fact]
    public async Task PostVectorize_WhenPythonReportsTimeout_ReturnsGatewayTimeout()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(
            _ => (200, PreprocessPayloads.SuccessBody()),
            _ => (200, ThresholdPayloads.SuccessBody()),
            _ => (504, VectorizePayloads.ErrorBody("vectorization_timeout", "tardó demasiado")));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var (projectId, imageId, maskId) = await CreateProjectWithMaskAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/vectorize", new VectorizeRequest(maskId));

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.Equal("timeout", error!.Code);
    }

    [Fact]
    public async Task GetVectorSvg_WhenVectorDoesNotExist_ReturnsNotFound()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(_ => (200, PreprocessPayloads.SuccessBody()));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/v1/projects/{Guid.NewGuid()}/images/{Guid.NewGuid()}/vectors/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<(Guid ProjectId, Guid ImageId, Guid MaskId)> CreateProjectWithMaskAsync(HttpClient client)
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

        return (uploadBody.ProjectId, uploadBody.ImageId, maskBody!.MaskId);
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
