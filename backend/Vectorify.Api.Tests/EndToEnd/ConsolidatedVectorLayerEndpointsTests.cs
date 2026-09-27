using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Vectorify.Api.Contracts;
using Vectorify.Api.Tests.TestSupport;

namespace Vectorify.Api.Tests.EndToEnd;

/// <summary>
/// Pruebas de integración HTTP de GET .../layers/consolidated (M2.1-S03):
/// levantan la Web API real (WebApplicationFactory) con un motor Python
/// simulado (FakePythonPreprocessServer) y ejercitan el flujo completo
/// upload -> color-palette/detect -> confirm -> layers -> consolidated,
/// confirmando que el contrato combina fill/color (M2-S02/M2.1-S01),
/// componentCount (M2-S03, null si no se calculó) y manufacturingOperation
/// (M2-S07, "unassigned" por defecto) sin volver a llamar a Python para
/// nada que ya esté persistido.
/// </summary>
public sealed class ConsolidatedVectorLayerEndpointsTests : IDisposable
{
    private readonly string _storageRoot = Path.Combine(Path.GetTempPath(), "vectorify-consolidated-layers-tests-" + Guid.NewGuid().ToString("n"));
    private readonly string _projectRegistryRoot = Path.Combine(Path.GetTempPath(), "vectorify-consolidated-layers-tests-registry-" + Guid.NewGuid().ToString("n"));

    [Fact]
    public async Task GetConsolidated_WhenLayersExistButNothingElseWasComputed_ReturnsDefaultsWithoutCallingPythonForComponentsOrOperations()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(_ => (200, "{}"));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var (projectId, imageId, paletteId) = await DetectConfirmAndGenerateLayersAsync(client);

        var response = await client.GetAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/color-palette/{paletteId}/layers/consolidated");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ConsolidatedVectorLayerSetResponse>();
        Assert.NotNull(body);
        Assert.Single(body!.Layers);

        var layer = body.Layers[0];
        // Fill = ColorHex (SvgFillWriter pinta el SVG con el color real tal cual).
        Assert.Equal(layer.ColorHex, layer.Fill);
        Assert.NotEqual(Guid.Empty, layer.Id);
        Assert.NotEqual(Guid.Empty, layer.VectorId);
        Assert.Contains($"/vectors/{layer.VectorId}", layer.SvgUrl);
        // M2-S03 nunca se calculó para esta capa todavía -> null, no 0.
        Assert.Null(layer.ComponentCount);
        // M2-S07 nunca asignó nada -> "unassigned", nunca un valor por defecto inventado.
        Assert.Equal("unassigned", layer.ManufacturingOperation);
        // Defaults documentados (M2.1-S03): la persistencia interactiva real es de M2.1-S07.
        Assert.True(layer.Visible);
        Assert.False(layer.Locked);
        Assert.Equal(0, layer.Order);
        // Validación raster-vs-vector (M2.1-S03) ya viene persistida, sin recalcular.
        Assert.True(layer.RasterValidation.OwnMismatchWithinTolerance);
        Assert.True(layer.RasterValidation.ContaminationWithinTolerance);

        Assert.Equal(0, pythonServer.ComponentsRequestCount);
    }

    [Fact]
    public async Task GetConsolidated_AfterComponentsAndOperationAreComputed_CombinesBothWithoutRecomputing()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(
            _ => (200, "{}"),
            respondComponents: _ => (200, ComponentPayloads.SuccessBody()));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var (projectId, imageId, paletteId) = await DetectConfirmAndGenerateLayersAsync(client);

        var layersResponse = await client.PostAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/color-palette/{paletteId}/layers", null);
        var layerSet = await layersResponse.Content.ReadFromJsonAsync<VectorLayerSetResponse>();
        var vectorId = layerSet!.Layers[0].VectorId;
        var groupId = layerSet.Layers[0].GroupId;

        var componentsResponse = await client.PostAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/vectors/{vectorId}/components", null);
        componentsResponse.EnsureSuccessStatusCode();

        var operationResponse = await client.PostAsJsonAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/color-palette/{paletteId}/layers/{groupId}/operation",
            new ManufacturingOperationRequest("cut"));
        operationResponse.EnsureSuccessStatusCode();

        var response = await client.GetAsync(
            $"/api/v1/projects/{projectId}/images/{imageId}/color-palette/{paletteId}/layers/consolidated");
        var body = await response.Content.ReadFromJsonAsync<ConsolidatedVectorLayerSetResponse>();

        var layer = Assert.Single(body!.Layers);
        Assert.Equal(1, layer.ComponentCount);
        Assert.Equal("cut", layer.ManufacturingOperation);
        // Se leyó lo ya persistido -- no se disparó un segundo cálculo de componentes.
        Assert.Equal(1, pythonServer.ComponentsRequestCount);
    }

    [Fact]
    public async Task GetConsolidated_WhenNoLayerSetWasEverGenerated_ReturnsNotFound()
    {
        await using var pythonServer = await FakePythonPreprocessServer.StartAsync(_ => (200, "{}"));
        await using var factory = CreateFactory(pythonServer.BaseUrl);
        var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/api/v1/projects/{Guid.NewGuid()}/images/{Guid.NewGuid()}/color-palette/{Guid.NewGuid()}/layers/consolidated");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.Equal("not_found", error!.Code);
    }

    private static async Task<(Guid ProjectId, Guid ImageId, Guid PaletteId)> DetectConfirmAndGenerateLayersAsync(HttpClient client)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(SampleImages.ValidPng1x1);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", "logo.png");

        var uploadResponse = await client.PostAsync("/api/v1/projects", content);
        uploadResponse.EnsureSuccessStatusCode();
        var uploadBody = await uploadResponse.Content.ReadFromJsonAsync<UploadImageResponse>();

        var detectResponse = await client.PostAsJsonAsync(
            $"/api/v1/projects/{uploadBody!.ProjectId}/images/{uploadBody.ImageId}/color-palette/detect",
            new ColorPaletteDetectRequest(null, null, null));
        detectResponse.EnsureSuccessStatusCode();
        var paletteBody = await detectResponse.Content.ReadFromJsonAsync<ColorPaletteResponse>();

        var confirmResponse = await client.PostAsync(
            $"/api/v1/projects/{uploadBody.ProjectId}/images/{uploadBody.ImageId}/color-palette/{paletteBody!.PaletteId}/confirm", null);
        confirmResponse.EnsureSuccessStatusCode();

        var layersResponse = await client.PostAsync(
            $"/api/v1/projects/{uploadBody.ProjectId}/images/{uploadBody.ImageId}/color-palette/{paletteBody.PaletteId}/layers", null);
        layersResponse.EnsureSuccessStatusCode();

        return (uploadBody.ProjectId, uploadBody.ImageId, paletteBody.PaletteId);
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
