using Microsoft.Extensions.Logging.Abstractions;
using Vectorify.Api.LayerLayout;
using Vectorify.Api.Tests.ManufacturingOperations;
using Vectorify.Api.Tests.VectorLayers;
using Vectorify.Api.VectorLayers;

namespace Vectorify.Api.Tests.LayerLayout;

/// <summary>
/// Pruebas unitarias de LayerLayoutService (M2.1-S07): validación contra el
/// conjunto de capas vigente, persistencia (setear y volver a leer da el
/// mismo valor), independencia entre Visible/Locked/Order (togglear uno no
/// afecta los otros dos ni las demás capas), reorder (persiste Order,
/// preserva Visible/Locked, y -- crítico -- NUNCA toca el VectorLayerSetVersion/
/// VectorId de ninguna capa), validación de Reorder con un conjunto de
/// groupId inválido, y que el layout NO se migra automáticamente si la
/// paleta se recalcula (PaletteVersion distinto) -- mismo criterio que
/// ManufacturingOperationServiceTests.
/// </summary>
public sealed class LayerLayoutServiceTests
{
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ImageId = Guid.NewGuid();
    private static readonly Guid PaletteId = Guid.NewGuid();
    private static readonly Guid GroupRedId = Guid.NewGuid();
    private static readonly Guid GroupGreenId = Guid.NewGuid();
    private static readonly Guid GroupBlueId = Guid.NewGuid();

    private static (LayerLayoutService Service, FakeVectorLayerService VectorLayerService, InMemoryLayerLayoutVersionRegistry Registry, VectorLayerSetVersion LayerSet)
        CreateService(int paletteVersion = 1, Guid? layerSetId = null)
    {
        var layerSet = new VectorLayerSetVersion(
            ProjectId,
            ImageId,
            Version: 1,
            LayerSetId: layerSetId ?? Guid.NewGuid(),
            PaletteId: PaletteId,
            PaletteVersion: paletteVersion,
            Layers: new List<VectorLayer>
            {
                new(GroupRedId, "Rojo", "#ff0000", 50.0, false, Guid.NewGuid(), FakePythonVectorLayerClient.DefaultRasterValidation()),
                new(GroupGreenId, "Verde", "#00ff00", 30.0, false, Guid.NewGuid(), FakePythonVectorLayerClient.DefaultRasterValidation()),
                new(GroupBlueId, "Azul", "#0000ff", 20.0, false, Guid.NewGuid(), FakePythonVectorLayerClient.DefaultRasterValidation()),
            },
            SourceWidthPx: 100,
            SourceHeightPx: 100,
            CreatedAt: DateTimeOffset.UtcNow);

        var vectorLayerService = new FakeVectorLayerService();
        vectorLayerService.AddLayerSet(layerSet);

        var registry = new InMemoryLayerLayoutVersionRegistry();
        var service = new LayerLayoutService(vectorLayerService, registry, NullLogger<LayerLayoutService>.Instance);

        return (service, vectorLayerService, registry, layerSet);
    }

    // ---- Validación ----

    [Fact]
    public async Task SetVisibleAsync_WhenNoLayerSetExistsForThatPalette_ReturnsNotFound()
    {
        var (service, _, _, _) = CreateService();

        var result = await service.SetVisibleAsync(ProjectId, ImageId, Guid.NewGuid(), GroupRedId, false, CancellationToken.None);

        var notFound = Assert.IsType<LayerLayoutResult.NotFound>(result);
        Assert.Equal("not_found", notFound.Code);
    }

    [Fact]
    public async Task SetLockedAsync_WhenGroupIdDoesNotExistInTheCurrentLayerSet_ReturnsNotFound()
    {
        var (service, _, _, _) = CreateService();

        var result = await service.SetLockedAsync(ProjectId, ImageId, PaletteId, Guid.NewGuid(), true, CancellationToken.None);

        var notFound = Assert.IsType<LayerLayoutResult.NotFound>(result);
        Assert.Equal("group_not_found", notFound.Code);
    }

    // ---- Persistencia: setear y volver a leer da el mismo valor ----

    [Fact]
    public async Task SetVisibleAsync_WhenValid_PersistsAndFindCurrentReadsBackTheSameValue()
    {
        var (service, _, _, _) = CreateService();

        var result = await service.SetVisibleAsync(ProjectId, ImageId, PaletteId, GroupRedId, false, CancellationToken.None);
        var ready = Assert.IsType<LayerLayoutResult.Ready>(result);
        Assert.Equal(1, ready.Record.Version);

        var current = service.FindCurrent(ProjectId, ImageId, PaletteId);
        Assert.NotNull(current);
        var entry = current!.Value.Layout!.Entries.Single(e => e.GroupId == GroupRedId);
        Assert.False(entry.Visible);
    }

    [Fact]
    public void FindCurrent_WhenLayerSetExistsButLayoutWasNeverTouched_ReturnsLayerSetWithNullLayout()
    {
        var (service, _, _, layerSet) = CreateService();

        var current = service.FindCurrent(ProjectId, ImageId, PaletteId);

        Assert.NotNull(current);
        Assert.Equal(layerSet.LayerSetId, current!.Value.LayerSet.LayerSetId);
        Assert.Null(current!.Value.Layout);
    }

    [Fact]
    public void FindCurrent_WhenNoLayerSetWasEverGenerated_ReturnsNull()
    {
        var (service, _, _, _) = CreateService();

        Assert.Null(service.FindCurrent(ProjectId, ImageId, Guid.NewGuid()));
    }

    // ---- Independencia entre Visible/Locked/Order ----

    [Fact]
    public async Task SetLockedAsync_TogglingLock_DoesNotAffectVisibleOrOrderOfTheSameLayer()
    {
        var (service, _, _, _) = CreateService();

        await service.SetVisibleAsync(ProjectId, ImageId, PaletteId, GroupRedId, false, CancellationToken.None);
        var result = await service.SetLockedAsync(ProjectId, ImageId, PaletteId, GroupRedId, true, CancellationToken.None);

        var ready = Assert.IsType<LayerLayoutResult.Ready>(result);
        var entry = ready.Record.Entries.Single(e => e.GroupId == GroupRedId);
        Assert.True(entry.Locked);
        // El Eye seguía apagado de antes: togglear Lock no lo prendió de vuelta.
        Assert.False(entry.Visible);
        // Order sigue siendo la posición original (0): Lock no reordena.
        Assert.Equal(0, entry.Order);
    }

    [Fact]
    public async Task SetVisibleAsync_TogglingOneLayer_PreservesTheOtherLayersEntriesIntact()
    {
        var (service, _, _, _) = CreateService();

        await service.SetLockedAsync(ProjectId, ImageId, PaletteId, GroupGreenId, true, CancellationToken.None);

        var result = await service.SetVisibleAsync(ProjectId, ImageId, PaletteId, GroupRedId, false, CancellationToken.None);
        var ready = Assert.IsType<LayerLayoutResult.Ready>(result);

        Assert.Equal(3, ready.Record.Entries.Count);
        Assert.False(ready.Record.Entries.Single(e => e.GroupId == GroupRedId).Visible);
        // Verde sigue bloqueada (de la mutación anterior) y VISIBLE por default -- intacta.
        var green = ready.Record.Entries.Single(e => e.GroupId == GroupGreenId);
        Assert.True(green.Locked);
        Assert.True(green.Visible);
    }

    // ---- Reorder ----

    [Fact]
    public async Task ReorderAsync_PersistsNewOrderAndNeverMutatesTheVectorLayerSetOrAnyVectorId()
    {
        var (service, _, _, layerSet) = CreateService();
        var originalVectorIdsByGroupId = layerSet.Layers.ToDictionary(l => l.GroupId, l => l.VectorId);

        var newOrder = new List<Guid> { GroupBlueId, GroupRedId, GroupGreenId };
        var result = await service.ReorderAsync(ProjectId, ImageId, PaletteId, newOrder, CancellationToken.None);

        var ready = Assert.IsType<LayerLayoutResult.Ready>(result);
        Assert.Equal(0, ready.Record.Entries.Single(e => e.GroupId == GroupBlueId).Order);
        Assert.Equal(1, ready.Record.Entries.Single(e => e.GroupId == GroupRedId).Order);
        Assert.Equal(2, ready.Record.Entries.Single(e => e.GroupId == GroupGreenId).Order);

        // El VectorLayerSetVersion devuelto es LITERALMENTE el mismo objeto de
        // siempre (reorder nunca regenera capas): mismo LayerSetId, y el
        // VectorId de cada capa (la geometría real, d/transform incluidos)
        // es exactamente el de antes del reorder -- reorder JAMÁS toca
        // geometría, ni siquiera indirectamente.
        Assert.Equal(layerSet.LayerSetId, ready.LayerSet.LayerSetId);
        foreach (var layer in ready.LayerSet.Layers)
        {
            Assert.Equal(originalVectorIdsByGroupId[layer.GroupId], layer.VectorId);
        }
    }

    [Fact]
    public async Task ReorderAsync_PreservesVisibleAndLockedOfEachLayer()
    {
        var (service, _, _, _) = CreateService();
        await service.SetVisibleAsync(ProjectId, ImageId, PaletteId, GroupRedId, false, CancellationToken.None);
        await service.SetLockedAsync(ProjectId, ImageId, PaletteId, GroupGreenId, true, CancellationToken.None);

        var result = await service.ReorderAsync(
            ProjectId, ImageId, PaletteId, new[] { GroupGreenId, GroupBlueId, GroupRedId }, CancellationToken.None);

        var ready = Assert.IsType<LayerLayoutResult.Ready>(result);
        Assert.False(ready.Record.Entries.Single(e => e.GroupId == GroupRedId).Visible);
        Assert.True(ready.Record.Entries.Single(e => e.GroupId == GroupGreenId).Locked);
    }

    [Theory]
    [MemberData(nameof(InvalidReorderPayloads))]
    public async Task ReorderAsync_WithAnInvalidGroupIdSet_ReturnsValidationFailed(Guid[] orderedGroupIds)
    {
        var (service, _, _, _) = CreateService();

        var result = await service.ReorderAsync(ProjectId, ImageId, PaletteId, orderedGroupIds, CancellationToken.None);

        var failed = Assert.IsType<LayerLayoutResult.ValidationFailed>(result);
        Assert.Equal("invalid_parameters", failed.Code);
    }

    public static IEnumerable<object[]> InvalidReorderPayloads()
    {
        // Falta una capa.
        yield return new object[] { new[] { GroupRedId, GroupGreenId } };
        // Un groupId duplicado (y por lo tanto otro ausente).
        yield return new object[] { new[] { GroupRedId, GroupRedId, GroupBlueId } };
        // Un groupId que no pertenece al conjunto vigente.
        yield return new object[] { new[] { GroupRedId, GroupGreenId, Guid.NewGuid() } };
    }

    // ---- Sin migración automática si la paleta se recalcula ----

    [Fact]
    public async Task FindCurrent_AfterThePaletteRegeneratesANewConfirmedVersion_DoesNotMigrateOldLayout()
    {
        var (service, vectorLayerService, _, _) = CreateService(paletteVersion: 1);

        await service.SetVisibleAsync(ProjectId, ImageId, PaletteId, GroupRedId, false, CancellationToken.None);

        var regenerated = new VectorLayerSetVersion(
            ProjectId, ImageId, Version: 2, LayerSetId: Guid.NewGuid(), PaletteId, PaletteVersion: 2,
            Layers: new List<VectorLayer>
            {
                new(GroupRedId, "Rojo", "#ff0000", 50.0, false, Guid.NewGuid(), FakePythonVectorLayerClient.DefaultRasterValidation()),
                new(GroupGreenId, "Verde", "#00ff00", 30.0, false, Guid.NewGuid(), FakePythonVectorLayerClient.DefaultRasterValidation()),
                new(GroupBlueId, "Azul", "#0000ff", 20.0, false, Guid.NewGuid(), FakePythonVectorLayerClient.DefaultRasterValidation()),
            },
            SourceWidthPx: 100, SourceHeightPx: 100, CreatedAt: DateTimeOffset.UtcNow);
        vectorLayerService.AddLayerSet(regenerated);

        var current = service.FindCurrent(ProjectId, ImageId, PaletteId);

        Assert.NotNull(current);
        Assert.Equal(2, current!.Value.LayerSet.PaletteVersion);
        Assert.Null(current!.Value.Layout);
    }

    // ---- LayerLayoutDefaults: valores default cuando nunca se tocó una capa ----

    [Fact]
    public void LayerLayoutDefaults_Resolve_WhenLayoutIsNull_ReturnsVisibleTrueLockedFalseInOriginalOrder()
    {
        var (_, _, _, layerSet) = CreateService();

        var resolved = LayerLayoutDefaults.Resolve(layerSet, layout: null);

        Assert.Equal(3, resolved.Count);
        Assert.All(resolved, e => Assert.True(e.Visible));
        Assert.All(resolved, e => Assert.False(e.Locked));
        Assert.Equal(new[] { GroupRedId, GroupGreenId, GroupBlueId }, resolved.OrderBy(e => e.Order).Select(e => e.GroupId));
    }
}
