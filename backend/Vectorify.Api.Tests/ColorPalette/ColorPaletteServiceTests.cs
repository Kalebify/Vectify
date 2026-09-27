using Microsoft.Extensions.Logging.Abstractions;
using Vectorify.Api.Clients;
using Vectorify.Api.ColorPalette;
using Vectorify.Api.Contracts;
using Vectorify.Api.Options;
using Vectorify.Api.Projects;
using Vectorify.Api.Tests.Projects;

namespace Vectorify.Api.Tests.ColorPalette;

/// <summary>
/// Pruebas unitarias de ColorPaletteService: localizar la imagen original +
/// validación + detección (caché/lock/storage/versionado, igual patrón que
/// SimplificationService) y las ediciones puras de metadata (merge/unmerge/
/// rename/confirm), sin depender de HTTP real ni del clustering de Python
/// (eso lo cubren los tests de Python). Ver spec.md M2-S01, criterios de
/// aceptación: "ciclo versión/cache/lock igual que los precedentes (cache
/// hit crea versión nueva, nunca reutiliza)".
/// </summary>
public sealed class ColorPaletteServiceTests
{
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ImageId = Guid.NewGuid();
    private const string SourceStorageKey = "project/image/original.png";

    private static (
        ColorPaletteService Service,
        FakeFileStorage Storage,
        InMemoryColorPaletteVersionRegistry Registry,
        FakePythonColorPaletteClient PythonClient) CreateService(bool withExistingProject = true)
    {
        var projectRegistry = new InMemoryProjectRegistry();
        if (withExistingProject)
        {
            projectRegistry.Save(new ProjectRecord(
                ProjectId, ImageId, "original.png", "image/png", 1024, 4, 4, "ready", SourceStorageKey, null, DateTimeOffset.UtcNow));
        }

        var storage = new FakeFileStorage();
        storage.Saved[SourceStorageKey] = System.Text.Encoding.UTF8.GetBytes("fake-original-bytes");

        var registry = new InMemoryColorPaletteVersionRegistry();
        var validator = new ColorPaletteParameterValidator(Microsoft.Extensions.Options.Options.Create(new ColorPaletteOptions()));
        var pythonClient = new FakePythonColorPaletteClient();

        var service = new ColorPaletteService(
            projectRegistry, registry, validator, pythonClient, storage, NullLogger<ColorPaletteService>.Instance);

        return (service, storage, registry, pythonClient);
    }

    private static ColorPaletteDetectRequest DetectRequest(Guid? paletteId = null, double? tolerance = null, int? maxColors = null) =>
        new(paletteId, tolerance, maxColors);

    // ---- DetectAsync ----

    [Fact]
    public async Task DetectAsync_WhenProjectDoesNotExist_ReturnsNotFound()
    {
        var (service, _, _, _) = CreateService(withExistingProject: false);

        var result = await service.DetectAsync(ProjectId, ImageId, DetectRequest(), CancellationToken.None);

        Assert.IsType<ColorPaletteResult.NotFound>(result);
    }

    [Fact]
    public async Task DetectAsync_WhenParametersAreInvalid_ReturnsValidationFailedWithoutCallingPython()
    {
        var (service, _, _, pythonClient) = CreateService();

        var result = await service.DetectAsync(ProjectId, ImageId, DetectRequest(tolerance: -5), CancellationToken.None);

        var failed = Assert.IsType<ColorPaletteResult.ValidationFailed>(result);
        Assert.Equal("invalid_parameters", failed.Code);
        Assert.Equal(0, pythonClient.CallCount);
    }

    [Fact]
    public async Task DetectAsync_WhenSuccessful_CreatesFirstVersionAndPersistsMasksAndPreview()
    {
        var (service, storage, registry, pythonClient) = CreateService();

        var result = await service.DetectAsync(ProjectId, ImageId, DetectRequest(), CancellationToken.None);

        var ready = Assert.IsType<ColorPaletteResult.Ready>(result);
        Assert.False(ready.FromCache);
        Assert.Equal(1, ready.Record.Version);
        Assert.Equal(2, ready.Record.Groups.Count);
        Assert.Equal(1, pythonClient.CallCount);
        Assert.False(ready.Record.IsConfirmed);
        Assert.True(storage.Saved.ContainsKey(ready.Record.QuantizedPreviewStorageKey));
        foreach (var group in ready.Record.Groups)
        {
            Assert.True(storage.Saved.ContainsKey(group.MaskStorageKey));
            Assert.Null(group.MergedFrom);
        }

        Assert.NotNull(registry.FindLatest(ProjectId, ImageId, ready.Record.PaletteId));
    }

    [Fact]
    public async Task DetectAsync_WithoutPaletteId_AlwaysStartsANewSessionAndNeverCaches()
    {
        var (service, _, _, pythonClient) = CreateService();

        var first = await service.DetectAsync(ProjectId, ImageId, DetectRequest(), CancellationToken.None);
        var second = await service.DetectAsync(ProjectId, ImageId, DetectRequest(), CancellationToken.None);

        var firstReady = Assert.IsType<ColorPaletteResult.Ready>(first);
        var secondReady = Assert.IsType<ColorPaletteResult.Ready>(second);
        Assert.NotEqual(firstReady.Record.PaletteId, secondReady.Record.PaletteId);
        Assert.False(secondReady.FromCache);
        Assert.Equal(2, pythonClient.CallCount);
    }

    [Fact]
    public async Task DetectAsync_WhenCalledAgainWithSamePaletteIdAndParams_ReturnsCachedWithoutCallingPythonAgain()
    {
        var (service, _, _, pythonClient) = CreateService();

        var first = await service.DetectAsync(ProjectId, ImageId, DetectRequest(), CancellationToken.None);
        var firstReady = Assert.IsType<ColorPaletteResult.Ready>(first);

        var second = await service.DetectAsync(
            ProjectId, ImageId, DetectRequest(paletteId: firstReady.Record.PaletteId), CancellationToken.None);
        var secondReady = Assert.IsType<ColorPaletteResult.Ready>(second);

        Assert.False(firstReady.FromCache);
        Assert.True(secondReady.FromCache);
        Assert.Equal(firstReady.Record.PaletteId, secondReady.Record.PaletteId);
        Assert.Equal(firstReady.Record.Version + 1, secondReady.Record.Version);
        Assert.Equal(1, pythonClient.CallCount);
    }

    [Fact]
    public async Task DetectAsync_WhenCalledAgainWithSamePaletteIdButDifferentParams_CallsPythonAgain()
    {
        var (service, _, _, pythonClient) = CreateService();

        var first = await service.DetectAsync(ProjectId, ImageId, DetectRequest(tolerance: 10), CancellationToken.None);
        var firstReady = Assert.IsType<ColorPaletteResult.Ready>(first);

        var second = await service.DetectAsync(
            ProjectId, ImageId, DetectRequest(paletteId: firstReady.Record.PaletteId, tolerance: 25), CancellationToken.None);
        var secondReady = Assert.IsType<ColorPaletteResult.Ready>(second);

        Assert.False(secondReady.FromCache);
        Assert.Equal(2, pythonClient.CallCount);
    }

    [Fact]
    public async Task DetectAsync_WithUnknownPaletteId_ReturnsNotFound()
    {
        var (service, _, _, _) = CreateService();

        var result = await service.DetectAsync(ProjectId, ImageId, DetectRequest(paletteId: Guid.NewGuid()), CancellationToken.None);

        Assert.IsType<ColorPaletteResult.NotFound>(result);
    }

    [Fact]
    public async Task DetectAsync_OnAConfirmedPalette_ReturnsConflict()
    {
        var (service, _, _, _) = CreateService();
        var detected = Assert.IsType<ColorPaletteResult.Ready>(
            await service.DetectAsync(ProjectId, ImageId, DetectRequest(), CancellationToken.None));
        await service.ConfirmAsync(ProjectId, ImageId, detected.Record.PaletteId, CancellationToken.None);

        var result = await service.DetectAsync(
            ProjectId, ImageId, DetectRequest(paletteId: detected.Record.PaletteId), CancellationToken.None);

        var conflict = Assert.IsType<ColorPaletteResult.Conflict>(result);
        Assert.Equal("palette_confirmed", conflict.Code);
    }

    [Fact]
    public async Task DetectAsync_WhenConcurrentRequestsForSamePaletteAndParams_CallsPythonOnlyOnce()
    {
        var (service, _, registry, pythonClient) = CreateService();
        var first = await service.DetectAsync(ProjectId, ImageId, DetectRequest(), CancellationToken.None);
        var paletteId = Assert.IsType<ColorPaletteResult.Ready>(first).Record.PaletteId;
        pythonClient.Delay = TimeSpan.FromMilliseconds(50);

        var task1 = service.DetectAsync(ProjectId, ImageId, DetectRequest(paletteId), CancellationToken.None);
        var task2 = service.DetectAsync(ProjectId, ImageId, DetectRequest(paletteId), CancellationToken.None);
        await Task.WhenAll(task1, task2);

        // Solo la llamada inicial (fuera de la sección concurrente) contó como MISS;
        // ambas llamadas concurrentes posteriores comparten la misma clave de
        // caché y deberían resolver a UN solo llamado adicional a Python como
        // máximo (no dos), igual criterio que SimplificationServiceTests.
        Assert.True(pythonClient.CallCount <= 2);
        var latest = registry.FindLatest(ProjectId, ImageId, paletteId);
        Assert.NotNull(latest);
    }

    [Fact]
    public async Task DetectAsync_WhenPythonReportsCorruptImage_ReturnsUpstreamErrorWithExpectedCode()
    {
        var (service, _, _, pythonClient) = CreateService();
        pythonClient.Respond = () => new PythonColorPaletteResult(
            PythonColorPaletteState.CorruptImage, null, null, null, null, null, null, "imagen corrupta");

        var result = await service.DetectAsync(ProjectId, ImageId, DetectRequest(), CancellationToken.None);

        var error = Assert.IsType<ColorPaletteResult.UpstreamError>(result);
        Assert.Equal("corrupt_image", error.Code);
    }

    [Fact]
    public async Task DetectAsync_WhenPythonTimesOut_ReturnsUpstreamErrorWithTimeoutCode()
    {
        var (service, _, _, pythonClient) = CreateService();
        pythonClient.Respond = () => new PythonColorPaletteResult(
            PythonColorPaletteState.Timeout, null, null, null, null, null, null, "tardó demasiado");

        var result = await service.DetectAsync(ProjectId, ImageId, DetectRequest(), CancellationToken.None);

        var error = Assert.IsType<ColorPaletteResult.UpstreamError>(result);
        Assert.Equal("timeout", error.Code);
    }

    // ---- MergeAsync ----

    private static async Task<ColorPaletteVersion> DetectAndGetRecordAsync(ColorPaletteService service)
    {
        var result = await service.DetectAsync(ProjectId, ImageId, DetectRequest(), CancellationToken.None);
        return Assert.IsType<ColorPaletteResult.Ready>(result).Record;
    }

    [Fact]
    public async Task MergeAsync_WithTwoGroups_CreatesNewVersionWithOneMergedGroup()
    {
        var (service, storage, _, _) = CreateService();
        var detected = await DetectAndGetRecordAsync(service);
        var groupIds = detected.Groups.Select(g => g.GroupId).ToList();

        var result = await service.MergeAsync(
            ProjectId, ImageId, detected.PaletteId, new ColorPaletteMergeRequest(groupIds, null), CancellationToken.None);

        var ready = Assert.IsType<ColorPaletteResult.Ready>(result);
        Assert.Equal(detected.Version + 1, ready.Record.Version);
        Assert.Single(ready.Record.Groups);
        var merged = ready.Record.Groups[0];
        Assert.Equal(detected.Groups.Sum(g => g.PixelCount), merged.PixelCount);
        Assert.NotNull(merged.MergedFrom);
        Assert.Equal(2, merged.MergedFrom!.Count);
        Assert.True(storage.Saved.ContainsKey(merged.MaskStorageKey));
        Assert.True(storage.Saved.ContainsKey(ready.Record.QuantizedPreviewStorageKey));
    }

    [Fact]
    public async Task MergeAsync_WithFewerThanTwoGroups_ReturnsValidationFailed()
    {
        var (service, _, _, _) = CreateService();
        var detected = await DetectAndGetRecordAsync(service);

        var result = await service.MergeAsync(
            ProjectId, ImageId, detected.PaletteId,
            new ColorPaletteMergeRequest(new[] { detected.Groups[0].GroupId }, null), CancellationToken.None);

        var failed = Assert.IsType<ColorPaletteResult.ValidationFailed>(result);
        Assert.Equal("invalid_parameters", failed.Code);
    }

    [Fact]
    public async Task MergeAsync_WithUnknownGroupId_ReturnsNotFound()
    {
        var (service, _, _, _) = CreateService();
        var detected = await DetectAndGetRecordAsync(service);

        var result = await service.MergeAsync(
            ProjectId, ImageId, detected.PaletteId,
            new ColorPaletteMergeRequest(new[] { detected.Groups[0].GroupId, Guid.NewGuid() }, null), CancellationToken.None);

        var notFound = Assert.IsType<ColorPaletteResult.NotFound>(result);
        Assert.Equal("group_not_found", notFound.Code);
    }

    [Fact]
    public async Task MergeAsync_WithUnknownPaletteId_ReturnsNotFound()
    {
        var (service, _, _, _) = CreateService();

        var result = await service.MergeAsync(
            ProjectId, ImageId, Guid.NewGuid(),
            new ColorPaletteMergeRequest(new[] { Guid.NewGuid(), Guid.NewGuid() }, null), CancellationToken.None);

        Assert.IsType<ColorPaletteResult.NotFound>(result);
    }

    [Fact]
    public async Task MergeAsync_OnAConfirmedPalette_ReturnsConflict()
    {
        var (service, _, _, _) = CreateService();
        var detected = await DetectAndGetRecordAsync(service);
        var confirmed = Assert.IsType<ColorPaletteResult.Ready>(
            await service.ConfirmAsync(ProjectId, ImageId, detected.PaletteId, CancellationToken.None));

        var result = await service.MergeAsync(
            ProjectId, ImageId, detected.PaletteId,
            new ColorPaletteMergeRequest(confirmed.Record.Groups.Select(g => g.GroupId).ToList(), null), CancellationToken.None);

        var conflict = Assert.IsType<ColorPaletteResult.Conflict>(result);
        Assert.Equal("palette_confirmed", conflict.Code);
    }

    [Fact]
    public async Task MergeAsync_WithCustomName_UsesItInsteadOfSynthesizedName()
    {
        var (service, _, _, _) = CreateService();
        var detected = await DetectAndGetRecordAsync(service);

        var result = await service.MergeAsync(
            ProjectId, ImageId, detected.PaletteId,
            new ColorPaletteMergeRequest(detected.Groups.Select(g => g.GroupId).ToList(), "Fondo"), CancellationToken.None);

        var ready = Assert.IsType<ColorPaletteResult.Ready>(result);
        Assert.Equal("Fondo", ready.Record.Groups[0].Name);
    }

    // ---- UnmergeAsync ----

    [Fact]
    public async Task UnmergeAsync_AfterAMerge_RestoresTheOriginalGroups()
    {
        var (service, _, _, _) = CreateService();
        var detected = await DetectAndGetRecordAsync(service);
        var originalGroupIds = detected.Groups.Select(g => g.GroupId).OrderBy(id => id).ToList();

        var mergedResult = await service.MergeAsync(
            ProjectId, ImageId, detected.PaletteId,
            new ColorPaletteMergeRequest(originalGroupIds, null), CancellationToken.None);
        var mergedGroupId = Assert.IsType<ColorPaletteResult.Ready>(mergedResult).Record.Groups[0].GroupId;

        var result = await service.UnmergeAsync(
            ProjectId, ImageId, detected.PaletteId, new ColorPaletteUnmergeRequest(mergedGroupId), CancellationToken.None);

        var ready = Assert.IsType<ColorPaletteResult.Ready>(result);
        Assert.Equal(2, ready.Record.Groups.Count);
        var restoredIds = ready.Record.Groups.Select(g => g.GroupId).OrderBy(id => id).ToList();
        Assert.Equal(originalGroupIds, restoredIds);
    }

    [Fact]
    public async Task UnmergeAsync_OnAGroupThatWasNeverMerged_ReturnsConflictNotMerged()
    {
        var (service, _, _, _) = CreateService();
        var detected = await DetectAndGetRecordAsync(service);

        var result = await service.UnmergeAsync(
            ProjectId, ImageId, detected.PaletteId, new ColorPaletteUnmergeRequest(detected.Groups[0].GroupId), CancellationToken.None);

        var conflict = Assert.IsType<ColorPaletteResult.Conflict>(result);
        Assert.Equal("not_merged", conflict.Code);
    }

    [Fact]
    public async Task UnmergeAsync_WithUnknownGroupId_ReturnsNotFound()
    {
        var (service, _, _, _) = CreateService();
        var detected = await DetectAndGetRecordAsync(service);

        var result = await service.UnmergeAsync(
            ProjectId, ImageId, detected.PaletteId, new ColorPaletteUnmergeRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.IsType<ColorPaletteResult.NotFound>(result);
    }

    // ---- RenameAsync ----

    [Fact]
    public async Task RenameAsync_WithValidName_UpdatesOnlyTheTargetGroup()
    {
        var (service, _, _, _) = CreateService();
        var detected = await DetectAndGetRecordAsync(service);
        var target = detected.Groups[0];
        var other = detected.Groups[1];

        var result = await service.RenameAsync(
            ProjectId, ImageId, detected.PaletteId, new ColorPaletteRenameRequest(target.GroupId, "Rojo principal"), CancellationToken.None);

        var ready = Assert.IsType<ColorPaletteResult.Ready>(result);
        Assert.Equal("Rojo principal", ready.Record.Groups.Single(g => g.GroupId == target.GroupId).Name);
        Assert.Equal(other.Name, ready.Record.Groups.Single(g => g.GroupId == other.GroupId).Name);
    }

    [Fact]
    public async Task RenameAsync_WithEmptyName_ReturnsValidationFailed()
    {
        var (service, _, _, _) = CreateService();
        var detected = await DetectAndGetRecordAsync(service);

        var result = await service.RenameAsync(
            ProjectId, ImageId, detected.PaletteId, new ColorPaletteRenameRequest(detected.Groups[0].GroupId, "   "), CancellationToken.None);

        var failed = Assert.IsType<ColorPaletteResult.ValidationFailed>(result);
        Assert.Equal("invalid_parameters", failed.Code);
    }

    [Fact]
    public async Task RenameAsync_WithUnknownGroupId_ReturnsNotFound()
    {
        var (service, _, _, _) = CreateService();
        var detected = await DetectAndGetRecordAsync(service);

        var result = await service.RenameAsync(
            ProjectId, ImageId, detected.PaletteId, new ColorPaletteRenameRequest(Guid.NewGuid(), "Nuevo"), CancellationToken.None);

        Assert.IsType<ColorPaletteResult.NotFound>(result);
    }

    [Fact]
    public async Task RenameAsync_OnAConfirmedPalette_ReturnsConflict()
    {
        var (service, _, _, _) = CreateService();
        var detected = await DetectAndGetRecordAsync(service);
        await service.ConfirmAsync(ProjectId, ImageId, detected.PaletteId, CancellationToken.None);

        var result = await service.RenameAsync(
            ProjectId, ImageId, detected.PaletteId, new ColorPaletteRenameRequest(detected.Groups[0].GroupId, "Nuevo"), CancellationToken.None);

        var conflict = Assert.IsType<ColorPaletteResult.Conflict>(result);
        Assert.Equal("palette_confirmed", conflict.Code);
    }

    // ---- ConfirmAsync ----

    [Fact]
    public async Task ConfirmAsync_MarksTheLatestVersionAsConfirmedAndAdvancesVersion()
    {
        var (service, _, _, _) = CreateService();
        var detected = await DetectAndGetRecordAsync(service);

        var result = await service.ConfirmAsync(ProjectId, ImageId, detected.PaletteId, CancellationToken.None);

        var ready = Assert.IsType<ColorPaletteResult.Ready>(result);
        Assert.True(ready.Record.IsConfirmed);
        Assert.Equal(detected.Version + 1, ready.Record.Version);
        Assert.Equal(detected.Groups.Count, ready.Record.Groups.Count);
    }

    [Fact]
    public async Task ConfirmAsync_WithUnknownPaletteId_ReturnsNotFound()
    {
        var (service, _, _, _) = CreateService();

        var result = await service.ConfirmAsync(ProjectId, ImageId, Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<ColorPaletteResult.NotFound>(result);
    }

    // ---- Heurística de fondo dominante (M2.1-S02) ----

    [Fact]
    public async Task DetectAsync_PreMarksTheLargestGroupThatTouchesTheBorderAsExcluded()
    {
        // FakePythonColorPaletteClient.DefaultSuccess: groups[0] (#ff0000) tiene TouchesBorder=true,
        // groups[1] (#00ff00) TouchesBorder=false -- groups[0] es el único candidato a fondo dominante.
        var (service, _, _, _) = CreateService();

        var detected = await DetectAndGetRecordAsync(service);

        Assert.True(detected.Groups[0].IsExcluded);
        Assert.False(detected.Groups[1].IsExcluded);
    }

    // ---- SetExclusionAsync ----

    [Fact]
    public async Task SetExclusionAsync_MarksTheTargetGroupWithoutAffectingOthers()
    {
        var (service, _, _, _) = CreateService();
        var detected = await DetectAndGetRecordAsync(service);
        var target = detected.Groups.First(g => !g.IsExcluded);
        var other = detected.Groups.First(g => g.GroupId != target.GroupId);

        var result = await service.SetExclusionAsync(
            ProjectId, ImageId, detected.PaletteId, new ColorPaletteSetExclusionRequest(target.GroupId, true), CancellationToken.None);

        var ready = Assert.IsType<ColorPaletteResult.Ready>(result);
        Assert.True(ready.Record.Groups.Single(g => g.GroupId == target.GroupId).IsExcluded);
        Assert.Equal(other.IsExcluded, ready.Record.Groups.Single(g => g.GroupId == other.GroupId).IsExcluded);
        Assert.Equal(detected.Version + 1, ready.Record.Version);
    }

    [Fact]
    public async Task SetExclusionAsync_CanRevertTheAutomaticBackgroundSuggestion()
    {
        // El usuario SIEMPRE puede cambiar la sugerencia automática -- ver spec.md, "el usuario manda".
        var (service, _, _, _) = CreateService();
        var detected = await DetectAndGetRecordAsync(service);
        var autoExcludedGroup = detected.Groups.Single(g => g.IsExcluded);

        var result = await service.SetExclusionAsync(
            ProjectId, ImageId, detected.PaletteId, new ColorPaletteSetExclusionRequest(autoExcludedGroup.GroupId, false), CancellationToken.None);

        var ready = Assert.IsType<ColorPaletteResult.Ready>(result);
        Assert.False(ready.Record.Groups.Single(g => g.GroupId == autoExcludedGroup.GroupId).IsExcluded);
    }

    [Fact]
    public async Task SetExclusionAsync_WithUnknownGroupId_ReturnsNotFound()
    {
        var (service, _, _, _) = CreateService();
        var detected = await DetectAndGetRecordAsync(service);

        var result = await service.SetExclusionAsync(
            ProjectId, ImageId, detected.PaletteId, new ColorPaletteSetExclusionRequest(Guid.NewGuid(), true), CancellationToken.None);

        var notFound = Assert.IsType<ColorPaletteResult.NotFound>(result);
        Assert.Equal("group_not_found", notFound.Code);
    }

    [Fact]
    public async Task SetExclusionAsync_WithUnknownPaletteId_ReturnsNotFound()
    {
        var (service, _, _, _) = CreateService();

        var result = await service.SetExclusionAsync(
            ProjectId, ImageId, Guid.NewGuid(), new ColorPaletteSetExclusionRequest(Guid.NewGuid(), true), CancellationToken.None);

        Assert.IsType<ColorPaletteResult.NotFound>(result);
    }

    [Fact]
    public async Task SetExclusionAsync_OnAConfirmedPalette_ReturnsConflict()
    {
        var (service, _, _, _) = CreateService();
        var detected = await DetectAndGetRecordAsync(service);
        await service.ConfirmAsync(ProjectId, ImageId, detected.PaletteId, CancellationToken.None);

        var result = await service.SetExclusionAsync(
            ProjectId, ImageId, detected.PaletteId, new ColorPaletteSetExclusionRequest(detected.Groups[0].GroupId, true), CancellationToken.None);

        var conflict = Assert.IsType<ColorPaletteResult.Conflict>(result);
        Assert.Equal("palette_confirmed", conflict.Code);
    }

    // ---- MergeAsync: herencia de IsExcluded ----

    [Fact]
    public async Task MergeAsync_WhenAllSelectedGroupsAreExcluded_MergedGroupIsAlsoExcluded()
    {
        var (service, _, _, _) = CreateService();
        var detected = await DetectAndGetRecordAsync(service);
        var groupIds = detected.Groups.Select(g => g.GroupId).ToList();
        // Excluir el segundo grupo también (el primero ya viene excluido por la heurística de fondo).
        var bothExcluded = await service.SetExclusionAsync(
            ProjectId, ImageId, detected.PaletteId, new ColorPaletteSetExclusionRequest(detected.Groups[1].GroupId, true), CancellationToken.None);
        var latest = Assert.IsType<ColorPaletteResult.Ready>(bothExcluded).Record;
        Assert.All(latest.Groups, g => Assert.True(g.IsExcluded));

        var result = await service.MergeAsync(
            ProjectId, ImageId, detected.PaletteId, new ColorPaletteMergeRequest(groupIds, null), CancellationToken.None);

        var ready = Assert.IsType<ColorPaletteResult.Ready>(result);
        Assert.True(ready.Record.Groups[0].IsExcluded);
    }

    [Fact]
    public async Task MergeAsync_WhenOnlySomeSelectedGroupsAreExcluded_MergedGroupIsNotExcluded()
    {
        // groups[0] viene excluido (fondo dominante), groups[1] no -- fusionar ambos NO debería
        // "esconder" el resultado combinado sin que el usuario lo note explícitamente.
        var (service, _, _, _) = CreateService();
        var detected = await DetectAndGetRecordAsync(service);
        Assert.True(detected.Groups[0].IsExcluded);
        Assert.False(detected.Groups[1].IsExcluded);
        var groupIds = detected.Groups.Select(g => g.GroupId).ToList();

        var result = await service.MergeAsync(
            ProjectId, ImageId, detected.PaletteId, new ColorPaletteMergeRequest(groupIds, null), CancellationToken.None);

        var ready = Assert.IsType<ColorPaletteResult.Ready>(result);
        Assert.False(ready.Record.Groups[0].IsExcluded);
    }

    // ---- Estabilidad de IDs durante la edición (spec.md, criterio explícito) ----

    [Fact]
    public async Task GroupId_OfAnUntouchedGroup_RemainsStableAcrossSuccessiveRenameAndExclusionEdits()
    {
        var (service, _, _, _) = CreateService();
        var detected = await DetectAndGetRecordAsync(service);
        var untouched = detected.Groups[1];
        var other = detected.Groups[0];

        var afterRename = Assert.IsType<ColorPaletteResult.Ready>(
            await service.RenameAsync(
                ProjectId, ImageId, detected.PaletteId, new ColorPaletteRenameRequest(other.GroupId, "Fondo renombrado"), CancellationToken.None))
            .Record;
        Assert.Contains(afterRename.Groups, g => g.GroupId == untouched.GroupId);

        var afterExclude = Assert.IsType<ColorPaletteResult.Ready>(
            await service.SetExclusionAsync(
                ProjectId, ImageId, detected.PaletteId, new ColorPaletteSetExclusionRequest(other.GroupId, false), CancellationToken.None))
            .Record;
        Assert.Contains(afterExclude.Groups, g => g.GroupId == untouched.GroupId);

        var stillUntouched = afterExclude.Groups.Single(g => g.GroupId == untouched.GroupId);
        Assert.Equal(untouched.Name, stillUntouched.Name);
        Assert.Equal(untouched.IsExcluded, stillUntouched.IsExcluded);
        Assert.Equal(untouched.ColorHex, stillUntouched.ColorHex);
    }

    // ---- FindLatest ----

    [Fact]
    public async Task FindLatest_AfterDetecting_ReturnsTheRecord()
    {
        var (service, _, _, _) = CreateService();
        var detected = await DetectAndGetRecordAsync(service);

        var found = service.FindLatest(ProjectId, ImageId, detected.PaletteId);

        Assert.NotNull(found);
        Assert.Equal(detected.PaletteId, found!.PaletteId);
    }

    [Fact]
    public void FindLatest_WhenSessionDoesNotExist_ReturnsNull()
    {
        var (service, _, _, _) = CreateService();

        var found = service.FindLatest(ProjectId, ImageId, Guid.NewGuid());

        Assert.Null(found);
    }
}
