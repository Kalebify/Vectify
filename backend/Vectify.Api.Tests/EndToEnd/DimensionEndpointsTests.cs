using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Vectify.Api.Contracts;
using Vectify.Api.Tests.TestSupport;

namespace Vectify.Api.Tests.EndToEnd;

/// <summary>
/// Pruebas de integración HTTP de POST .../dimensions/apply y GET
/// .../dimensions/{id}: levantan la Web API real (WebApplicationFactory) con
/// almacenamiento local apuntando a un directorio temporal. A diferencia de
/// Simplification/Check, esta etapa NUNCA llama al motor Python (ver
/// Vectify.Api.Dimensioning.SvgDimensionWriter) -- el FakePythonPreprocessServer
/// solo se usa para las etapas previas del pipeline (preprocess/threshold/
/// vectorize/simplify) necesarias para llegar a un SVG de origen. Cubre
/// directamente las "Pruebas" obligatorias de spec.md M1-S09: aspect ratios
/// distintos, valores límite/inválidos, round-trip y verificación de medidas.
/// </summary>
public sealed class DimensionEndpointsTests : IDisposable
{
    private readonly string _storageRoot = Path.Combine(Path.GetTempPath(), "vectify-dimensions-tests-" + Guid.NewGuid().ToString("n"));
    private readonly string _projectRegistryRoot = Path.Combine(Path.GetTempPath(), "vectify-dimensions-tests-registry-" + Guid.NewGuid().ToString("n"));

    [Fact]
    public async Task PostDimensionsApply_WhenLockedWithSquareSource_DerivesTheMissingSideAndPersistsANewVersion()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(
            _ => (200, PreprocessPayloads.SuccessBody()),
            _ => (200, ThresholdPayloads.SuccessBody()),
            _ => (200, VectorizePayloads.SuccessBody(width: 10, height: 10)));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var (projectId, imageId, vectorId) = await CreateProjectWithVectorAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/dimensions/apply",
            new DimensionRequest("vector", vectorId, 150, null, true));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DimensionResponse>();
        Assert.NotNull(body);
        Assert.Equal(150, body!.WidthMm);
        Assert.Equal(150, body.HeightMm);
        Assert.Equal(1, body.Version);
        Assert.False(body.Cached);
        Assert.Equal(10, body.SourceWidthPx);
        Assert.Equal(10, body.SourceHeightPx);

        // El vector de origen sigue disponible sin cambios -- esta etapa nunca lo modifica.
        var originalResponse = await client.GetAsync($"/api/v1/projects/{projectId}/images/{imageId}/vectors/{vectorId}");
        Assert.Equal(HttpStatusCode.OK, originalResponse.StatusCode);
    }

    [Theory]
    [InlineData(10, 10, 500.0, null, 500.0, 500.0)] // 1:1
    [InlineData(200, 50, 400.0, null, 400.0, 100.0)] // muy ancho
    [InlineData(50, 200, null, 400.0, 100.0, 400.0)] // muy alto
    public async Task PostDimensionsApply_ForDifferentAspectRatios_DerivesTheMissingSideProportionally(
        int sourceWidthPx, int sourceHeightPx, double? widthMm, double? heightMm, double expectedWidthMm, double expectedHeightMm)
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(
            _ => (200, PreprocessPayloads.SuccessBody()),
            _ => (200, ThresholdPayloads.SuccessBody()),
            _ => (200, VectorizePayloads.SuccessBody(width: sourceWidthPx, height: sourceHeightPx)));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var (projectId, imageId, vectorId) = await CreateProjectWithVectorAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/dimensions/apply",
            new DimensionRequest("vector", vectorId, widthMm, heightMm, true));

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<DimensionResponse>();
        Assert.Equal(expectedWidthMm, body!.WidthMm, precision: 6);
        Assert.Equal(expectedHeightMm, body.HeightMm, precision: 6);
    }

    [Fact]
    public async Task GetDimensionedSvg_RoundTrip_ViewBoxAndMmWidthHeightAreMathematicallyConsistent()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(
            _ => (200, PreprocessPayloads.SuccessBody()),
            _ => (200, ThresholdPayloads.SuccessBody()),
            _ => (200, VectorizePayloads.SuccessBody(width: 20, height: 40)));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var (projectId, imageId, vectorId) = await CreateProjectWithVectorAsync(client);

        var applyResponse = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/dimensions/apply",
            new DimensionRequest("vector", vectorId, null, 300, true));
        applyResponse.EnsureSuccessStatusCode();
        var applyBody = await applyResponse.Content.ReadFromJsonAsync<DimensionResponse>();

        // "Reabrir": GET del SVG ya persistido, como lo haría un visor externo.
        var svgResponse = await client.GetAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/dimensions/{applyBody!.DimensionId}");
        svgResponse.EnsureSuccessStatusCode();
        Assert.Equal("image/svg+xml", svgResponse.Content.Headers.ContentType?.MediaType);

        var svgText = await svgResponse.Content.ReadAsStringAsync();
        var root = XDocument.Parse(svgText).Root!;

        Assert.Equal($"{applyBody.WidthMm.ToString("0.###", CultureInfo.InvariantCulture)}mm", root.Attribute("width")!.Value);
        Assert.Equal($"{applyBody.HeightMm.ToString("0.###", CultureInfo.InvariantCulture)}mm", root.Attribute("height")!.Value);

        var viewBoxParts = root.Attribute("viewBox")!.Value.Split(' ');
        var viewBoxWidth = double.Parse(viewBoxParts[2], CultureInfo.InvariantCulture);
        var viewBoxHeight = double.Parse(viewBoxParts[3], CultureInfo.InvariantCulture);

        Assert.Equal(20, viewBoxWidth);
        Assert.Equal(40, viewBoxHeight);
        Assert.Equal(applyBody.WidthMm / applyBody.HeightMm, viewBoxWidth / viewBoxHeight, precision: 6);

        // El path original sigue intacto -- nunca se tocó ningún `d`.
        Assert.Contains("<path d=\"M2,2 L8,2 L8,8 L2,8 Z\"", svgText);
    }

    [Fact]
    public async Task PostDimensionsApply_WhenUnlockedWithDifferentAspect_DeformsAndSetsPreserveAspectRatioNone()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(
            _ => (200, PreprocessPayloads.SuccessBody()),
            _ => (200, ThresholdPayloads.SuccessBody()),
            _ => (200, VectorizePayloads.SuccessBody(width: 10, height: 10)));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var (projectId, imageId, vectorId) = await CreateProjectWithVectorAsync(client);

        var applyResponse = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/dimensions/apply",
            new DimensionRequest("vector", vectorId, 400, 20, false));
        applyResponse.EnsureSuccessStatusCode();
        var applyBody = await applyResponse.Content.ReadFromJsonAsync<DimensionResponse>();
        Assert.False(applyBody!.LockAspectRatio);
        Assert.Equal(400, applyBody.WidthMm);
        Assert.Equal(20, applyBody.HeightMm);

        var svgResponse = await client.GetAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/dimensions/{applyBody.DimensionId}");
        var svgText = await svgResponse.Content.ReadAsStringAsync();
        Assert.Contains("preserveAspectRatio=\"none\"", svgText);
    }

    [Fact]
    public async Task PostDimensionsApply_WhenAppliedOverASimplification_UsesItsOwnSourceId()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(
            _ => (200, PreprocessPayloads.SuccessBody()),
            _ => (200, ThresholdPayloads.SuccessBody()),
            _ => (200, VectorizePayloads.SuccessBody()),
            respondSimplify: _ => (200, SimplifyPayloads.SuccessBody()));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var (projectId, imageId, vectorId) = await CreateProjectWithVectorAsync(client);

        var simplifyResponse = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/simplify/apply",
            new SimplifyRequest(vectorId, "medium", null));
        simplifyResponse.EnsureSuccessStatusCode();
        var simplifyBody = await simplifyResponse.Content.ReadFromJsonAsync<SimplifyResponse>();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/dimensions/apply",
            new DimensionRequest("simplification", simplifyBody!.SimplificationId, 50, null, true));

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<DimensionResponse>();
        Assert.Equal("simplification", body!.SourceKind);
        Assert.Equal(simplifyBody.SimplificationId, body.SourceId);
    }

    [Fact]
    public async Task PostDimensionsApply_CalledTwiceWithSameParams_SecondCallIsCachedAndAdvancesVersion()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(
            _ => (200, PreprocessPayloads.SuccessBody()),
            _ => (200, ThresholdPayloads.SuccessBody()),
            _ => (200, VectorizePayloads.SuccessBody(width: 10, height: 10)));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var (projectId, imageId, vectorId) = await CreateProjectWithVectorAsync(client);
        var request = new DimensionRequest("vector", vectorId, 100, null, true);

        var first = await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/images/{imageId}/dimensions/apply", request);
        var second = await client.PostAsJsonAsync($"/api/v1/projects/{projectId}/images/{imageId}/dimensions/apply", request);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var firstBody = await first.Content.ReadFromJsonAsync<DimensionResponse>();
        var secondBody = await second.Content.ReadFromJsonAsync<DimensionResponse>();
        Assert.Equal(firstBody!.DimensionId, secondBody!.DimensionId);
        Assert.False(firstBody.Cached);
        Assert.True(secondBody.Cached);
        Assert.Equal(firstBody.Version + 1, secondBody.Version);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    public async Task PostDimensionsApply_WhenWidthMmIsZeroOrNegative_ReturnsBadRequestWithInvalidParameters(double widthMm)
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(
            _ => (200, PreprocessPayloads.SuccessBody()),
            _ => (200, ThresholdPayloads.SuccessBody()),
            _ => (200, VectorizePayloads.SuccessBody()));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var (projectId, imageId, vectorId) = await CreateProjectWithVectorAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/dimensions/apply",
            new DimensionRequest("vector", vectorId, widthMm, null, true));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.Equal("invalid_parameters", error!.Code);
    }

    [Fact]
    public async Task PostDimensionsApply_WhenWidthMmExceedsTheConfiguredMaximum_ReturnsBadRequest()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(
            _ => (200, PreprocessPayloads.SuccessBody()),
            _ => (200, ThresholdPayloads.SuccessBody()),
            _ => (200, VectorizePayloads.SuccessBody()));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var (projectId, imageId, vectorId) = await CreateProjectWithVectorAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/dimensions/apply",
            new DimensionRequest("vector", vectorId, 1000.1, null, true));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.Equal("invalid_parameters", error!.Code);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1000.0)]
    public async Task PostDimensionsApply_WhenWidthMmIsAtTheBoundary_Succeeds(double widthMm)
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(
            _ => (200, PreprocessPayloads.SuccessBody()),
            _ => (200, ThresholdPayloads.SuccessBody()),
            _ => (200, VectorizePayloads.SuccessBody()));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var (projectId, imageId, vectorId) = await CreateProjectWithVectorAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/dimensions/apply",
            new DimensionRequest("vector", vectorId, widthMm, null, true));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task PostDimensionsApply_WhenLockedAndBothWidthAndHeightProvided_ReturnsBadRequest()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(
            _ => (200, PreprocessPayloads.SuccessBody()),
            _ => (200, ThresholdPayloads.SuccessBody()),
            _ => (200, VectorizePayloads.SuccessBody()));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var (projectId, imageId, vectorId) = await CreateProjectWithVectorAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/dimensions/apply",
            new DimensionRequest("vector", vectorId, 100, 50, true));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.Equal("invalid_parameters", error!.Code);
    }

    [Fact]
    public async Task PostDimensionsApply_WhenUnlockedAndOnlyOneValueProvided_ReturnsBadRequest()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(
            _ => (200, PreprocessPayloads.SuccessBody()),
            _ => (200, ThresholdPayloads.SuccessBody()),
            _ => (200, VectorizePayloads.SuccessBody()));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var (projectId, imageId, vectorId) = await CreateProjectWithVectorAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/dimensions/apply",
            new DimensionRequest("vector", vectorId, 100, null, false));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.Equal("invalid_parameters", error!.Code);
    }

    [Fact]
    public async Task PostDimensionsApply_WhenSourceKindIsUnknown_ReturnsBadRequest()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(
            _ => (200, PreprocessPayloads.SuccessBody()),
            _ => (200, ThresholdPayloads.SuccessBody()),
            _ => (200, VectorizePayloads.SuccessBody()));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var (projectId, imageId, vectorId) = await CreateProjectWithVectorAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/dimensions/apply",
            new DimensionRequest("mask", vectorId, 100, null, true));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostDimensionsApply_WhenSourceVectorDoesNotExist_ReturnsNotFound()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(_ => (200, PreprocessPayloads.SuccessBody()));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var (projectId, imageId, _) = await CreateProjectWithVectorAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/dimensions/apply",
            new DimensionRequest("vector", Guid.NewGuid(), 100, null, true));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetDimensionedSvg_WhenDimensionIdDoesNotExist_ReturnsNotFound()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(_ => (200, PreprocessPayloads.SuccessBody()));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var (projectId, imageId, _) = await CreateProjectWithVectorAsync(client);

        var response = await client.GetAsync($"/api/v1/projects/{projectId}/images/{imageId}/dimensions/{Guid.NewGuid()}");

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
