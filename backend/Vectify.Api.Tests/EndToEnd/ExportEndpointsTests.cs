using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Vectify.Api.Contracts;
using Vectify.Api.Tests.TestSupport;

namespace Vectify.Api.Tests.EndToEnd;

/// <summary>
/// Pruebas de integración HTTP de GET .../export (M1-S10): levantan la Web
/// API real (WebApplicationFactory) con almacenamiento local apuntando a un
/// directorio temporal. Cubre directamente las "Pruebas" obligatorias de
/// spec.md: reabrir el SVG exportado y compararlo byte a byte contra la
/// versión de origen (nunca se modifica geometría), nombres de archivo con
/// caracteres especiales, y export repetido (determinista, no falla, no deja
/// estado inconsistente -- este endpoint no persiste nada, así que
/// "inconsistente" se reduce a "sirve siempre los mismos bytes").
/// </summary>
public sealed class ExportEndpointsTests : IDisposable
{
    private readonly string _storageRoot = Path.Combine(Path.GetTempPath(), "vectify-export-tests-" + Guid.NewGuid().ToString("n"));
    private readonly string _projectRegistryRoot = Path.Combine(Path.GetTempPath(), "vectify-export-tests-registry-" + Guid.NewGuid().ToString("n"));

    [Fact]
    public async Task GetExport_ForAVector_ReturnsExactlyTheSameBytesAsTheOriginalSvgWithAttachmentHeaders()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(
            _ => (200, PreprocessPayloads.SuccessBody()),
            _ => (200, ThresholdPayloads.SuccessBody()),
            _ => (200, VectorizePayloads.SuccessBody()));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var (projectId, imageId, vectorId) = await CreateProjectWithVectorAsync(client, "logo.png");

        var originalResponse = await client.GetAsync($"/api/v1/projects/{projectId}/images/{imageId}/vectors/{vectorId}");
        originalResponse.EnsureSuccessStatusCode();
        var originalBytes = await originalResponse.Content.ReadAsByteArrayAsync();

        var exportResponse = await client.GetAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/export?sourceKind=vector&sourceId={vectorId}");

        Assert.Equal(HttpStatusCode.OK, exportResponse.StatusCode);
        Assert.Equal("image/svg+xml", exportResponse.Content.Headers.ContentType?.MediaType);

        var exportedBytes = await exportResponse.Content.ReadAsByteArrayAsync();
        Assert.Equal(originalBytes, exportedBytes);

        var contentDisposition = exportResponse.Content.Headers.ContentDisposition;
        Assert.NotNull(contentDisposition);
        Assert.Equal("attachment", contentDisposition!.DispositionType);
        Assert.Equal("logo-vector-v1.svg", contentDisposition.FileName);
        Assert.Equal("logo-vector-v1.svg", contentDisposition.FileNameStar);
    }

    [Fact]
    public async Task GetExport_ForASimplification_ReturnsExactlyTheSameBytesAsTheOriginalSvg()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(
            _ => (200, PreprocessPayloads.SuccessBody()),
            _ => (200, ThresholdPayloads.SuccessBody()),
            _ => (200, VectorizePayloads.SuccessBody()),
            respondSimplify: _ => (200, SimplifyPayloads.SuccessBody()));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var (projectId, imageId, vectorId) = await CreateProjectWithVectorAsync(client, "logo.png");

        var simplifyResponse = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/simplify/apply",
            new SimplifyRequest(vectorId, "medium", null));
        simplifyResponse.EnsureSuccessStatusCode();
        var simplifyBody = await simplifyResponse.Content.ReadFromJsonAsync<SimplifyResponse>();

        var originalResponse = await client.GetAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/simplifications/{simplifyBody!.SimplificationId}");
        var originalBytes = await originalResponse.Content.ReadAsByteArrayAsync();

        var exportResponse = await client.GetAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/export?sourceKind=simplification&sourceId={simplifyBody.SimplificationId}");

        Assert.Equal(HttpStatusCode.OK, exportResponse.StatusCode);
        var exportedBytes = await exportResponse.Content.ReadAsByteArrayAsync();
        Assert.Equal(originalBytes, exportedBytes);
    }

    [Fact]
    public async Task GetExport_ForADimension_ReturnsExactlyTheSameBytesAsTheOriginalSvg()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(
            _ => (200, PreprocessPayloads.SuccessBody()),
            _ => (200, ThresholdPayloads.SuccessBody()),
            _ => (200, VectorizePayloads.SuccessBody(width: 20, height: 40)));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var (projectId, imageId, vectorId) = await CreateProjectWithVectorAsync(client, "logo.png");

        var dimensionResponse = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/dimensions/apply",
            new DimensionRequest("vector", vectorId, null, 300, true));
        dimensionResponse.EnsureSuccessStatusCode();
        var dimensionBody = await dimensionResponse.Content.ReadFromJsonAsync<DimensionResponse>();

        var originalResponse = await client.GetAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/dimensions/{dimensionBody!.DimensionId}");
        var originalBytes = await originalResponse.Content.ReadAsByteArrayAsync();

        var exportResponse = await client.GetAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/export?sourceKind=dimension&sourceId={dimensionBody.DimensionId}");

        Assert.Equal(HttpStatusCode.OK, exportResponse.StatusCode);
        var exportedBytes = await exportResponse.Content.ReadAsByteArrayAsync();
        Assert.Equal(originalBytes, exportedBytes);

        var contentDisposition = exportResponse.Content.Headers.ContentDisposition;
        Assert.NotNull(contentDisposition);
        Assert.Equal("logo-dimension-v1.svg", contentDisposition!.FileName);
    }

    [Theory]
    [InlineData("diseño láser (final) #1.png", "diseño láser (final) #1-vector-v1.svg")]
    [InlineData("mi archivo con espacios.png", "mi archivo con espacios-vector-v1.svg")]
    [InlineData("100% señalización & piezas.png", "100% señalización & piezas-vector-v1.svg")]
    public async Task GetExport_WithSpecialCharactersInTheOriginalFileName_ProducesASanitizedDownloadName(
        string uploadedFileName, string expectedFileName)
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(
            _ => (200, PreprocessPayloads.SuccessBody()),
            _ => (200, ThresholdPayloads.SuccessBody()),
            _ => (200, VectorizePayloads.SuccessBody()));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var (projectId, imageId, vectorId) = await CreateProjectWithVectorAsync(client, uploadedFileName);

        var exportResponse = await client.GetAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/export?sourceKind=vector&sourceId={vectorId}");

        Assert.Equal(HttpStatusCode.OK, exportResponse.StatusCode);
        var contentDisposition = exportResponse.Content.Headers.ContentDisposition;
        Assert.NotNull(contentDisposition);
        Assert.Equal("attachment", contentDisposition!.DispositionType);

        // filename* (RFC 5987) lleva el nombre real -- con tildes/espacios/
        // símbolos benignos preservados -- para navegadores modernos.
        Assert.Equal(expectedFileName, contentDisposition.FileNameStar);

        // filename= (respaldo ASCII, para clientes sin soporte de filename*)
        // también debe estar presente -- lo arma SetHttpFileName a partir del
        // mismo nombre sanitizado.
        Assert.False(string.IsNullOrEmpty(contentDisposition.FileName));
    }

    [Fact]
    public async Task GetExport_CalledTwiceForTheSameVersion_IsDeterministicAndReturnsTheSameBytesBothTimes()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(
            _ => (200, PreprocessPayloads.SuccessBody()),
            _ => (200, ThresholdPayloads.SuccessBody()),
            _ => (200, VectorizePayloads.SuccessBody()));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var (projectId, imageId, vectorId) = await CreateProjectWithVectorAsync(client, "logo.png");

        var first = await client.GetAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/export?sourceKind=vector&sourceId={vectorId}");
        var second = await client.GetAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/export?sourceKind=vector&sourceId={vectorId}");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var firstBytes = await first.Content.ReadAsByteArrayAsync();
        var secondBytes = await second.Content.ReadAsByteArrayAsync();
        Assert.Equal(firstBytes, secondBytes);

        var firstDisposition = first.Content.Headers.GetValues("Content-Disposition").Single();
        var secondDisposition = second.Content.Headers.GetValues("Content-Disposition").Single();
        Assert.Equal(firstDisposition, secondDisposition);
    }

    [Fact]
    public async Task GetExport_WhenSourceKindIsUnknown_ReturnsBadRequest()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(
            _ => (200, PreprocessPayloads.SuccessBody()),
            _ => (200, ThresholdPayloads.SuccessBody()),
            _ => (200, VectorizePayloads.SuccessBody()));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var (projectId, imageId, vectorId) = await CreateProjectWithVectorAsync(client, "logo.png");

        var response = await client.GetAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/export?sourceKind=mask&sourceId={vectorId}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.Equal("invalid_parameters", error!.Code);
    }

    [Fact]
    public async Task GetExport_WhenSourceIdDoesNotExist_ReturnsNotFound()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(_ => (200, PreprocessPayloads.SuccessBody()));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var (projectId, imageId, _) = await CreateProjectWithVectorAsync(client, "logo.png");

        var response = await client.GetAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/export?sourceKind=vector&sourceId={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.Equal("not_found", error!.Code);
    }

    [Fact]
    public async Task GetExport_NeverModifiesTheOriginSvg()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(
            _ => (200, PreprocessPayloads.SuccessBody()),
            _ => (200, ThresholdPayloads.SuccessBody()),
            _ => (200, VectorizePayloads.SuccessBody()));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var (projectId, imageId, vectorId) = await CreateProjectWithVectorAsync(client, "logo.png");

        await client.GetAsync($"/api/v1/projects/{projectId}/images/{imageId}/export?sourceKind=vector&sourceId={vectorId}");

        var originalResponse = await client.GetAsync($"/api/v1/projects/{projectId}/images/{imageId}/vectors/{vectorId}");
        Assert.Equal(HttpStatusCode.OK, originalResponse.StatusCode);
        var svgText = await originalResponse.Content.ReadAsStringAsync();
        Assert.Contains("<path d=\"M2,2 L8,2 L8,8 L2,8 Z\"", svgText);
    }

    private static async Task<(Guid ProjectId, Guid ImageId, Guid VectorId)> CreateProjectWithVectorAsync(
        HttpClient client, string uploadedFileName)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(SampleImages.ValidPng1x1);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", uploadedFileName);

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
