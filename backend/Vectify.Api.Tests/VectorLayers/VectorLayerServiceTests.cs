using Microsoft.Extensions.Logging.Abstractions;
using Vectify.Api.Clients;
using Vectify.Api.ColorPalette;
using Vectify.Api.Contracts;
using Vectify.Api.Options;
using Vectify.Api.Projects;
using Vectify.Api.Tests.ColorPalette;
using Vectify.Api.Tests.Projects;
using Vectify.Api.VectorLayers;
using Vectify.Api.Vectorization;

namespace Vectify.Api.Tests.VectorLayers;

/// <summary>
/// Pruebas unitarias de VectorLayerService: precondición (paleta debe existir
/// y estar confirmada, sin llamar a Python si no), ciclo cache/lock/versionado
/// (mismo criterio que ColorPaletteService/VectorizationService) y que cada
/// capa generada queda registrada como una VectorVersion normal, reutilizable
/// por el resto del pipeline. Ver spec.md M2-S02, criterios de aceptación.
/// </summary>
public sealed class VectorLayerServiceTests
{
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ImageId = Guid.NewGuid();
    private const string SourceStorageKey = "project/image/original.png";

    private static (
        VectorLayerService Service,
        ColorPaletteService PaletteService,
        FakeFileStorage Storage,
        InMemoryVectorLayerSetRegistry LayerSetRegistry,
        InMemoryVectorVersionRegistry VectorRegistry,
        FakePythonVectorLayerClient PythonClient) CreateService()
    {
        var projectRegistry = new InMemoryProjectRegistry();
        projectRegistry.Save(new ProjectRecord(
            ProjectId, ImageId, "original.png", "image/png", 1024, 4, 4, "ready", SourceStorageKey, null, DateTimeOffset.UtcNow));

        var storage = new FakeFileStorage();
        storage.Saved[SourceStorageKey] = System.Text.Encoding.UTF8.GetBytes("fake-original-bytes");

        var paletteRegistry = new InMemoryColorPaletteVersionRegistry();
        var paletteValidator = new ColorPaletteParameterValidator(Microsoft.Extensions.Options.Options.Create(new ColorPaletteOptions()));
        var paletteClient = new FakePythonColorPaletteClient();
        var paletteService = new ColorPaletteService(
            projectRegistry, paletteRegistry, paletteValidator, paletteClient, storage, NullLogger<ColorPaletteService>.Instance);

        var layerSetRegistry = new InMemoryVectorLayerSetRegistry();
        var vectorRegistry = new InMemoryVectorVersionRegistry();
        var pythonLayerClient = new FakePythonVectorLayerClient();

        var service = new VectorLayerService(
            paletteService, layerSetRegistry, vectorRegistry, pythonLayerClient, storage, NullLogger<VectorLayerService>.Instance);

        return (service, paletteService, storage, layerSetRegistry, vectorRegistry, pythonLayerClient);
    }

    private static async Task<ColorPaletteVersion> DetectPaletteAsync(ColorPaletteService paletteService) =>
        Assert.IsType<ColorPaletteResult.Ready>(
            await paletteService.DetectAsync(ProjectId, ImageId, new ColorPaletteDetectRequest(null, null, null), CancellationToken.None)
        ).Record;

    private static async Task<ColorPaletteVersion> DetectAndConfirmPaletteAsync(ColorPaletteService paletteService)
    {
        var detected = await DetectPaletteAsync(paletteService);
        var confirmed = await paletteService.ConfirmAsync(ProjectId, ImageId, detected.PaletteId, CancellationToken.None);
        return Assert.IsType<ColorPaletteResult.Ready>(confirmed).Record;
    }

    // ---- Precondición ----

    [Fact]
    public async Task GenerateLayersAsync_WhenPaletteDoesNotExist_ReturnsNotFoundWithoutCallingPython()
    {
        var (service, _, _, _, _, pythonClient) = CreateService();

        var result = await service.GenerateLayersAsync(ProjectId, ImageId, Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<VectorLayerSetResult.NotFound>(result);
        Assert.Equal(0, pythonClient.CallCount);
    }

    [Fact]
    public async Task GenerateLayersAsync_WhenPaletteIsNotConfirmed_ReturnsConflictWithoutCallingPython()
    {
        var (service, paletteService, _, _, _, pythonClient) = CreateService();
        var detected = await DetectPaletteAsync(paletteService);

        var result = await service.GenerateLayersAsync(ProjectId, ImageId, detected.PaletteId, CancellationToken.None);

        var conflict = Assert.IsType<VectorLayerSetResult.Conflict>(result);
        Assert.Equal("palette_not_confirmed", conflict.Code);
        Assert.Equal(0, pythonClient.CallCount);
    }

    // ---- Generación exitosa ----

    [Fact]
    public async Task GenerateLayersAsync_WhenSuccessful_CreatesFirstVersionWithOneLayerPerGroup()
    {
        var (service, paletteService, storage, layerSetRegistry, _, pythonClient) = CreateService();
        var confirmed = await DetectAndConfirmPaletteAsync(paletteService);

        var result = await service.GenerateLayersAsync(ProjectId, ImageId, confirmed.PaletteId, CancellationToken.None);

        var ready = Assert.IsType<VectorLayerSetResult.Ready>(result);
        Assert.False(ready.FromCache);
        Assert.Equal(1, ready.Record.Version);
        Assert.Equal(confirmed.Groups.Count, ready.Record.Layers.Count);
        Assert.Equal(1, pythonClient.CallCount);
        Assert.NotNull(layerSetRegistry.FindLatest(ProjectId, ImageId, confirmed.PaletteId));

        foreach (var layer in ready.Record.Layers)
        {
            var sourceGroup = confirmed.Groups.Single(g => g.GroupId == layer.GroupId);
            Assert.Equal(sourceGroup.Name, layer.Name);
            Assert.Equal(sourceGroup.ColorHex, layer.ColorHex);
            Assert.NotEqual(Guid.Empty, layer.VectorId);
        }

        // Cada capa persistió su propio SVG bajo una clave distinta.
        var svgKeys = storage.Saved.Keys.Where(k => k.Contains("/vector-layers/")).ToList();
        Assert.Equal(confirmed.Groups.Count, svgKeys.Count);
    }

    [Fact]
    public async Task GenerateLayersAsync_EachLayerIsRegisteredAsAReusableVectorVersion()
    {
        var (service, paletteService, _, _, vectorRegistry, _) = CreateService();
        var confirmed = await DetectAndConfirmPaletteAsync(paletteService);

        var result = await service.GenerateLayersAsync(ProjectId, ImageId, confirmed.PaletteId, CancellationToken.None);
        var ready = Assert.IsType<VectorLayerSetResult.Ready>(result);

        foreach (var layer in ready.Record.Layers)
        {
            // Reutiliza EXACTAMENTE el mismo IVectorVersionRegistry/VectorVersion
            // de M1-S05 -- el resto del pipeline (Simplification/Check/
            // Dimension/Export) puede resolver esta capa como cualquier otro
            // vector, por su VectorId, sin saber que vino de una capa de color.
            var vectorVersion = vectorRegistry.FindByVectorId(ProjectId, ImageId, layer.VectorId);
            Assert.NotNull(vectorVersion);
            Assert.Equal(layer.GroupId, vectorVersion!.SourceMaskId);
        }
    }

    [Fact]
    public async Task GenerateLayersAsync_LayersShareTheSameSourceCanvasDimensionsAsThePalette()
    {
        var (service, paletteService, _, _, _, _) = CreateService();
        var confirmed = await DetectAndConfirmPaletteAsync(paletteService);

        var result = await service.GenerateLayersAsync(ProjectId, ImageId, confirmed.PaletteId, CancellationToken.None);
        var ready = Assert.IsType<VectorLayerSetResult.Ready>(result);

        Assert.Equal(confirmed.SourceWidthPx, ready.Record.SourceWidthPx);
        Assert.Equal(confirmed.SourceHeightPx, ready.Record.SourceHeightPx);
    }

    // ---- Ciclo cache/versión ----

    [Fact]
    public async Task GenerateLayersAsync_WhenCalledAgainForSamePaletteVersion_ReturnsCachedWithoutCallingPythonAgain()
    {
        var (service, paletteService, _, _, _, pythonClient) = CreateService();
        var confirmed = await DetectAndConfirmPaletteAsync(paletteService);

        var first = await service.GenerateLayersAsync(ProjectId, ImageId, confirmed.PaletteId, CancellationToken.None);
        var second = await service.GenerateLayersAsync(ProjectId, ImageId, confirmed.PaletteId, CancellationToken.None);

        var firstReady = Assert.IsType<VectorLayerSetResult.Ready>(first);
        var secondReady = Assert.IsType<VectorLayerSetResult.Ready>(second);

        Assert.False(firstReady.FromCache);
        Assert.True(secondReady.FromCache);
        Assert.Equal(1, pythonClient.CallCount);

        // Nunca retrocede ni "re-sirve" la misma versión: siempre avanza,
        // reutilizando el mismo LayerSetId y los mismos VectorId ya generados.
        Assert.Equal(firstReady.Record.Version + 1, secondReady.Record.Version);
        Assert.Equal(firstReady.Record.LayerSetId, secondReady.Record.LayerSetId);
        Assert.Equal(
            firstReady.Record.Layers.Select(l => l.VectorId).OrderBy(id => id),
            secondReady.Record.Layers.Select(l => l.VectorId).OrderBy(id => id));
    }

    [Fact]
    public async Task GenerateLayersAsync_WhenConcurrentRequestsForSamePalette_CallsPythonOnlyOnce()
    {
        var (service, paletteService, _, layerSetRegistry, _, pythonClient) = CreateService();
        var confirmed = await DetectAndConfirmPaletteAsync(paletteService);
        pythonClient.Delay = TimeSpan.FromMilliseconds(50);

        var task1 = service.GenerateLayersAsync(ProjectId, ImageId, confirmed.PaletteId, CancellationToken.None);
        var task2 = service.GenerateLayersAsync(ProjectId, ImageId, confirmed.PaletteId, CancellationToken.None);
        await Task.WhenAll(task1, task2);

        Assert.Equal(1, pythonClient.CallCount);
        Assert.NotNull(layerSetRegistry.FindLatest(ProjectId, ImageId, confirmed.PaletteId));
    }

    // ---- Errores upstream ----

    [Fact]
    public async Task GenerateLayersAsync_WhenPythonTimesOut_ReturnsUpstreamErrorWithTimeoutCode()
    {
        var (service, paletteService, _, _, _, pythonClient) = CreateService();
        var confirmed = await DetectAndConfirmPaletteAsync(paletteService);
        pythonClient.Respond = _ => new PythonVectorLayerBatchResult(PythonVectorLayerState.Timeout, null, "tardó demasiado");

        var result = await service.GenerateLayersAsync(ProjectId, ImageId, confirmed.PaletteId, CancellationToken.None);

        var error = Assert.IsType<VectorLayerSetResult.UpstreamError>(result);
        Assert.Equal("timeout", error.Code);
    }

    [Fact]
    public async Task GenerateLayersAsync_WhenPythonIsUnavailable_ReturnsUpstreamErrorWithEngineUnavailableCode()
    {
        var (service, paletteService, _, _, _, pythonClient) = CreateService();
        var confirmed = await DetectAndConfirmPaletteAsync(paletteService);
        pythonClient.Respond = _ => new PythonVectorLayerBatchResult(PythonVectorLayerState.Unavailable, null, "sin conexión");

        var result = await service.GenerateLayersAsync(ProjectId, ImageId, confirmed.PaletteId, CancellationToken.None);

        var error = Assert.IsType<VectorLayerSetResult.UpstreamError>(result);
        Assert.Equal("engine_unavailable", error.Code);
    }

    [Fact]
    public async Task GenerateLayersAsync_WhenPythonOmitsALayerForAGroup_ReturnsUpstreamErrorWithInvalidResponseCode()
    {
        var (service, paletteService, _, _, _, pythonClient) = CreateService();
        var confirmed = await DetectAndConfirmPaletteAsync(paletteService);
        pythonClient.Respond = masks => new PythonVectorLayerBatchResult(
            PythonVectorLayerState.Success,
            masks.Take(masks.Count - 1).Select(mask => new PythonVectorLayerItemResult(
                mask.GroupId,
                "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\"><path d=\"M2,2 L8,2 L8,8 L2,8 Z\"/></svg>",
                "image/svg+xml", 10, 10, new VectorMetrics(1, 4, new VectorBounds(2, 2, 8, 8, 6, 6)))).ToList(),
            Message: null);

        var result = await service.GenerateLayersAsync(ProjectId, ImageId, confirmed.PaletteId, CancellationToken.None);

        var error = Assert.IsType<VectorLayerSetResult.UpstreamError>(result);
        Assert.Equal("invalid_response", error.Code);
    }

    // ---- FindLatest ----

    [Fact]
    public async Task FindLatest_AfterGenerating_ReturnsTheRecord()
    {
        var (service, paletteService, _, _, _, _) = CreateService();
        var confirmed = await DetectAndConfirmPaletteAsync(paletteService);
        var generated = await service.GenerateLayersAsync(ProjectId, ImageId, confirmed.PaletteId, CancellationToken.None);
        var ready = Assert.IsType<VectorLayerSetResult.Ready>(generated);

        var found = service.FindLatest(ProjectId, ImageId, confirmed.PaletteId);

        Assert.NotNull(found);
        Assert.Equal(ready.Record.LayerSetId, found!.LayerSetId);
    }

    [Fact]
    public void FindLatest_WhenNeverGenerated_ReturnsNull()
    {
        var (service, _, _, _, _, _) = CreateService();

        var found = service.FindLatest(ProjectId, ImageId, Guid.NewGuid());

        Assert.Null(found);
    }
}
