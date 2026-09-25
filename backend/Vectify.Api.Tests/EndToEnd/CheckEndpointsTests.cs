using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Vectify.Api.Contracts;
using Vectify.Api.Tests.TestSupport;

namespace Vectify.Api.Tests.EndToEnd;

/// <summary>
/// Pruebas de integración HTTP de POST .../check: levantan la Web API real
/// (WebApplicationFactory) con almacenamiento local apuntando a un directorio
/// temporal y un motor Python simulado (FakePythonPreprocessServer, sin
/// análisis geométrico real -- eso lo cubren services/python-engine/tests y
/// Vectify.Api.Tests.Checking.CheckServiceTests). Cubre el contrato completo
/// (upload -> ... -> vector -> check, y también sobre una simplificación ya
/// aplicada), que SIEMPRE responde 200 sin persistir nada, y los errores
/// controlados que puede reportar Python.
/// </summary>
public sealed class CheckEndpointsTests : IDisposable
{
    private readonly string _storageRoot = Path.Combine(Path.GetTempPath(), "vectify-check-tests-" + Guid.NewGuid().ToString("n"));
    private readonly string _projectRegistryRoot = Path.Combine(Path.GetTempPath(), "vectify-check-tests-registry-" + Guid.NewGuid().ToString("n"));

    [Fact]
    public async Task PostCheck_WhenVectorExists_ReturnsIssuesWithoutPersistingAnything()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(
            _ => (200, PreprocessPayloads.SuccessBody()),
            _ => (200, ThresholdPayloads.SuccessBody()),
            _ => (200, VectorizePayloads.SuccessBody()),
            respondCheck: _ => (200, CheckPayloads.SuccessBody()));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var (projectId, imageId, vectorId) = await CreateProjectWithVectorAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/check",
            new CheckRequest("vector", vectorId, null, null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CheckResponse>();
        Assert.NotNull(body);
        Assert.Equal("vector", body!.SourceKind);
        Assert.Equal(vectorId, body.SourceId);
        Assert.Equal(1, body.Summary.OpenPathCount);
        Assert.Equal(1, body.Summary.DuplicateGroupCount);
        Assert.Equal(2, body.Issues.Count);
        Assert.Equal(1, pythonServer.CheckRequestCount);

        // El vector de origen sigue disponible sin cambios -- el checker nunca lo modifica.
        var originalResponse = await client.GetAsync($"/api/v1/projects/{projectId}/images/{imageId}/vectors/{vectorId}");
        Assert.Equal(HttpStatusCode.OK, originalResponse.StatusCode);
    }

    [Fact]
    public async Task PostCheck_WhenSimplificationExists_ReturnsIssuesForTheSimplifiedSvg()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(
            _ => (200, PreprocessPayloads.SuccessBody()),
            _ => (200, ThresholdPayloads.SuccessBody()),
            _ => (200, VectorizePayloads.SuccessBody()),
            respondSimplify: _ => (200, SimplifyPayloads.SuccessBody()),
            respondCheck: _ => (200, CheckPayloads.SuccessBody(openPathCount: 0, duplicateGroupCount: 0, issuesJson: "[]")));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var (projectId, imageId, vectorId) = await CreateProjectWithVectorAsync(client);

        var simplifyResponse = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/simplify/apply",
            new SimplifyRequest(vectorId, "medium", null));
        simplifyResponse.EnsureSuccessStatusCode();
        var simplifyBody = await simplifyResponse.Content.ReadFromJsonAsync<SimplifyResponse>();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/check",
            new CheckRequest("simplification", simplifyBody!.SimplificationId, null, null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CheckResponse>();
        Assert.Equal("simplification", body!.SourceKind);
        Assert.Equal(simplifyBody.SimplificationId, body.SourceId);
        Assert.Empty(body.Issues);
    }

    [Fact]
    public async Task PostCheck_CalledTwice_NeverCachesAndAlwaysCallsPythonAgain()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(
            _ => (200, PreprocessPayloads.SuccessBody()),
            _ => (200, ThresholdPayloads.SuccessBody()),
            _ => (200, VectorizePayloads.SuccessBody()),
            respondCheck: _ => (200, CheckPayloads.SuccessBody()));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var (projectId, imageId, vectorId) = await CreateProjectWithVectorAsync(client);
        var request = new CheckRequest("vector", vectorId, null, null);

        await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/images/{imageId}/check", request);
        await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/images/{imageId}/check", request);

        Assert.Equal(2, pythonServer.CheckRequestCount);
    }

    [Fact]
    public async Task PostCheck_IsDeterministic_SameResponseAcrossRepeatedRequests()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(
            _ => (200, PreprocessPayloads.SuccessBody()),
            _ => (200, ThresholdPayloads.SuccessBody()),
            _ => (200, VectorizePayloads.SuccessBody()),
            respondCheck: _ => (200, CheckPayloads.SuccessBody()));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var (projectId, imageId, vectorId) = await CreateProjectWithVectorAsync(client);
        var request = new CheckRequest("vector", vectorId, null, null);

        var first = await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/images/{imageId}/check", request);
        var second = await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/images/{imageId}/check", request);

        var firstBody = await first.Content.ReadAsStringAsync();
        var secondBody = await second.Content.ReadAsStringAsync();
        Assert.Equal(firstBody, secondBody);
    }

    [Fact]
    public async Task PostCheck_WhenSourceKindIsUnknown_ReturnsBadRequestWithInvalidParameters()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(
            _ => (200, PreprocessPayloads.SuccessBody()),
            _ => (200, ThresholdPayloads.SuccessBody()),
            _ => (200, VectorizePayloads.SuccessBody()));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var (projectId, imageId, vectorId) = await CreateProjectWithVectorAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/check",
            new CheckRequest("mask", vectorId, null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.Equal("invalid_parameters", error!.Code);
        Assert.Equal(0, pythonServer.CheckRequestCount);
    }

    [Fact]
    public async Task PostCheck_WhenSourceVectorDoesNotExist_ReturnsNotFound()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(_ => (200, PreprocessPayloads.SuccessBody()));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var (projectId, imageId, _) = await CreateProjectWithVectorAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/check",
            new CheckRequest("vector", Guid.NewGuid(), null, null));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostCheck_WhenPythonReportsInvalidInputSvg_ReturnsBadRequest()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(
            _ => (200, PreprocessPayloads.SuccessBody()),
            _ => (200, ThresholdPayloads.SuccessBody()),
            _ => (200, VectorizePayloads.SuccessBody()),
            respondCheck: _ => (400, CheckPayloads.ErrorBody("invalid_input_svg", "SVG de entrada corrupto")));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var (projectId, imageId, vectorId) = await CreateProjectWithVectorAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/check",
            new CheckRequest("vector", vectorId, null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.Equal("invalid_input_svg", error!.Code);
    }

    [Fact]
    public async Task PostCheck_WhenCloseGapRatioIsOutOfRange_ReturnsBadRequestWithInvalidParameters()
    {
        // Mismo criterio que SimplificationEndpoints: la validación propia de
        // Vectify.Api (antes de llamar a Python) responde 400, no 422 -- 422
        // es el código que Python devuelve por SU PROPIA validación (mapeado
        // como UpstreamError en un flujo distinto, ver StatusCodeFor).
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(
            _ => (200, PreprocessPayloads.SuccessBody()),
            _ => (200, ThresholdPayloads.SuccessBody()),
            _ => (200, VectorizePayloads.SuccessBody()));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var (projectId, imageId, vectorId) = await CreateProjectWithVectorAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/check",
            new CheckRequest("vector", vectorId, 0.9, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.Equal("invalid_parameters", error!.Code);
        Assert.Equal(0, pythonServer.CheckRequestCount);
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
