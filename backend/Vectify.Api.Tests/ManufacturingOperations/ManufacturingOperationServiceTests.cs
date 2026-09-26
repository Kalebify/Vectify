using Microsoft.Extensions.Logging.Abstractions;
using Vectify.Api.ManufacturingOperations;
using Vectify.Api.VectorLayers;

namespace Vectify.Api.Tests.ManufacturingOperations;

/// <summary>
/// Pruebas unitarias de ManufacturingOperationService (M2-S07): validación
/// contra el conjunto de capas vigente de la paleta, los 3 únicos valores
/// aceptados, persistencia (asignar y volver a leer da el mismo valor),
/// cambios (reasignar crea una versión nueva sin mutar la anterior, que
/// queda en el historial), capas ignoradas, combinación corte+grabado en el
/// mismo conjunto, y que las asignaciones NO se migran automáticamente si la
/// paleta se recalcula (PaletteVersion distinto) -- mismo criterio que
/// ComponentGroupServiceTests para M2-S05.
/// </summary>
public sealed class ManufacturingOperationServiceTests
{
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ImageId = Guid.NewGuid();
    private static readonly Guid PaletteId = Guid.NewGuid();
    private static readonly Guid GroupRedId = Guid.NewGuid();
    private static readonly Guid GroupGreenId = Guid.NewGuid();

    private static (ManufacturingOperationService Service, FakeVectorLayerService VectorLayerService, InMemoryManufacturingOperationVersionRegistry Registry, VectorLayerSetVersion LayerSet)
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
                new(GroupRedId, "Rojo", "#ff0000", 60.0, false, Guid.NewGuid()),
                new(GroupGreenId, "Verde", "#00ff00", 40.0, false, Guid.NewGuid()),
            },
            SourceWidthPx: 100,
            SourceHeightPx: 100,
            CreatedAt: DateTimeOffset.UtcNow);

        var vectorLayerService = new FakeVectorLayerService();
        vectorLayerService.AddLayerSet(layerSet);

        var registry = new InMemoryManufacturingOperationVersionRegistry();
        var service = new ManufacturingOperationService(vectorLayerService, registry, NullLogger<ManufacturingOperationService>.Instance);

        return (service, vectorLayerService, registry, layerSet);
    }

    // ---- Validación ----

    [Fact]
    public async Task AssignAsync_WhenNoLayerSetExistsForThatPalette_ReturnsNotFound()
    {
        var (service, _, _, _) = CreateService();
        var otherPaletteId = Guid.NewGuid();

        var result = await service.AssignAsync(ProjectId, ImageId, otherPaletteId, GroupRedId, "cut", CancellationToken.None);

        var notFound = Assert.IsType<ManufacturingOperationResult.NotFound>(result);
        Assert.Equal("not_found", notFound.Code);
    }

    [Fact]
    public async Task AssignAsync_WhenGroupIdDoesNotExistInTheCurrentLayerSet_ReturnsNotFound()
    {
        var (service, _, _, _) = CreateService();

        var result = await service.AssignAsync(ProjectId, ImageId, PaletteId, Guid.NewGuid(), "cut", CancellationToken.None);

        var notFound = Assert.IsType<ManufacturingOperationResult.NotFound>(result);
        Assert.Equal("group_not_found", notFound.Code);
    }

    [Theory]
    [InlineData((string?)null)]
    [InlineData("")]
    [InlineData("potencia")]
    [InlineData("unassigned")]
    public async Task AssignAsync_WithAnyValueOtherThanTheThreeExactWireValues_ReturnsValidationFailed(string? operation)
    {
        var (service, _, _, _) = CreateService();

        var result = await service.AssignAsync(ProjectId, ImageId, PaletteId, GroupRedId, operation, CancellationToken.None);

        var failed = Assert.IsType<ManufacturingOperationResult.ValidationFailed>(result);
        Assert.Equal("invalid_parameters", failed.Code);
    }

    [Fact]
    public async Task AssignAsync_IsCaseInsensitiveAndTrimsWhitespace()
    {
        var (service, _, _, _) = CreateService();

        var result = await service.AssignAsync(ProjectId, ImageId, PaletteId, GroupRedId, "  CUT  ", CancellationToken.None);

        var ready = Assert.IsType<ManufacturingOperationResult.Ready>(result);
        Assert.Equal(ManufacturingOperationKind.Cut, ready.Record.Assignments[0].Operation);
    }

    // ---- Persistencia ----

    [Fact]
    public async Task AssignAsync_WhenValid_CreatesFirstVersionAndFindCurrentReadsBackTheSameValue()
    {
        var (service, _, _, _) = CreateService();

        var result = await service.AssignAsync(ProjectId, ImageId, PaletteId, GroupRedId, "cut", CancellationToken.None);
        var ready = Assert.IsType<ManufacturingOperationResult.Ready>(result);
        Assert.Equal(1, ready.Record.Version);

        var current = service.FindCurrent(ProjectId, ImageId, PaletteId);
        Assert.NotNull(current);
        var assignments = current!.Value.Assignments;
        Assert.NotNull(assignments);
        Assert.Single(assignments!.Assignments);
        Assert.Equal(GroupRedId, assignments!.Assignments[0].GroupId);
        Assert.Equal(ManufacturingOperationKind.Cut, assignments!.Assignments[0].Operation);
    }

    [Fact]
    public void FindCurrent_WhenLayerSetExistsButNothingWasEverAssigned_ReturnsLayerSetWithNullAssignments()
    {
        var (service, _, _, layerSet) = CreateService();

        var current = service.FindCurrent(ProjectId, ImageId, PaletteId);

        Assert.NotNull(current);
        Assert.Equal(layerSet.LayerSetId, current!.Value.LayerSet.LayerSetId);
        Assert.Null(current!.Value.Assignments);
    }

    [Fact]
    public void FindCurrent_WhenNoLayerSetWasEverGenerated_ReturnsNull()
    {
        var (service, _, _, _) = CreateService();

        Assert.Null(service.FindCurrent(ProjectId, ImageId, Guid.NewGuid()));
    }

    // ---- Cambios: reasignar crea una versión nueva, la anterior queda en el historial ----

    [Fact]
    public async Task AssignAsync_ReassigningTheSameLayer_CreatesANewVersionWithoutMutatingThePreviousRecordInstance()
    {
        var (service, _, _, _) = CreateService();

        var first = await service.AssignAsync(ProjectId, ImageId, PaletteId, GroupRedId, "cut", CancellationToken.None);
        var firstReady = Assert.IsType<ManufacturingOperationResult.Ready>(first);

        var second = await service.AssignAsync(ProjectId, ImageId, PaletteId, GroupRedId, "engrave", CancellationToken.None);
        var secondReady = Assert.IsType<ManufacturingOperationResult.Ready>(second);

        // La versión avanza...
        Assert.Equal(1, firstReady.Record.Version);
        Assert.Equal(2, secondReady.Record.Version);

        // ...y el objeto de la PRIMERA versión, que el caller sigue teniendo en
        // mano, nunca fue mutado: sigue mostrando "cut" para siempre -- ESE es
        // el "historial" (nunca se muta un ManufacturingOperationSetVersion ya
        // devuelto/guardado, cada cambio es un record NUEVO).
        Assert.Equal(ManufacturingOperationKind.Cut, firstReady.Record.Assignments[0].Operation);
        Assert.Equal(ManufacturingOperationKind.Engrave, secondReady.Record.Assignments[0].Operation);

        // FindCurrent (la fuente de verdad "vigente") solo ve la última.
        var current = service.FindCurrent(ProjectId, ImageId, PaletteId);
        Assert.Equal(2, current!.Value.Assignments!.Version);
        Assert.Equal(ManufacturingOperationKind.Engrave, current!.Value.Assignments!.Assignments[0].Operation);
    }

    [Fact]
    public async Task AssignAsync_ReassigningOneLayer_PreservesTheOtherLayersAssignmentsIntact()
    {
        var (service, _, _, _) = CreateService();

        await service.AssignAsync(ProjectId, ImageId, PaletteId, GroupRedId, "cut", CancellationToken.None);
        await service.AssignAsync(ProjectId, ImageId, PaletteId, GroupGreenId, "engrave", CancellationToken.None);

        // Reasignar Rojo de Corte a Ignorar no debe tocar la asignación de Verde.
        var result = await service.AssignAsync(ProjectId, ImageId, PaletteId, GroupRedId, "ignore", CancellationToken.None);
        var ready = Assert.IsType<ManufacturingOperationResult.Ready>(result);

        Assert.Equal(2, ready.Record.Assignments.Count);
        Assert.Equal(ManufacturingOperationKind.Ignore, ready.Record.Assignments.Single(a => a.GroupId == GroupRedId).Operation);
        Assert.Equal(ManufacturingOperationKind.Engrave, ready.Record.Assignments.Single(a => a.GroupId == GroupGreenId).Operation);
    }

    // ---- Capas ignoradas ----

    [Fact]
    public async Task AssignAsync_MarkingALayerAsIgnore_IsPersistedAndReadBackAsIgnore()
    {
        var (service, _, _, _) = CreateService();

        await service.AssignAsync(ProjectId, ImageId, PaletteId, GroupRedId, "ignore", CancellationToken.None);

        var current = service.FindCurrent(ProjectId, ImageId, PaletteId);
        Assert.Equal(ManufacturingOperationKind.Ignore, current!.Value.Assignments!.Assignments.Single(a => a.GroupId == GroupRedId).Operation);
    }

    // ---- Combinación corte+grabado en el mismo conjunto ----

    [Fact]
    public async Task AssignAsync_WithOneLayerCutAndAnotherEngrave_BothCoexistDistinctlyInTheSameSetVersion()
    {
        var (service, _, _, _) = CreateService();

        await service.AssignAsync(ProjectId, ImageId, PaletteId, GroupRedId, "cut", CancellationToken.None);
        var result = await service.AssignAsync(ProjectId, ImageId, PaletteId, GroupGreenId, "engrave", CancellationToken.None);

        var ready = Assert.IsType<ManufacturingOperationResult.Ready>(result);
        Assert.Equal(2, ready.Record.Assignments.Count);
        Assert.Equal(ManufacturingOperationKind.Cut, ready.Record.Assignments.Single(a => a.GroupId == GroupRedId).Operation);
        Assert.Equal(ManufacturingOperationKind.Engrave, ready.Record.Assignments.Single(a => a.GroupId == GroupGreenId).Operation);
    }

    // ---- Sin migración automática si la paleta se recalcula ----

    [Fact]
    public async Task FindCurrent_AfterThePaletteRegeneratesANewConfirmedVersion_DoesNotMigrateOldAssignments()
    {
        var (service, vectorLayerService, _, _) = CreateService(paletteVersion: 1);

        await service.AssignAsync(ProjectId, ImageId, PaletteId, GroupRedId, "cut", CancellationToken.None);

        // Simula que M2-S02 recalculó la paleta: nueva versión confirmada
        // (PaletteVersion 2), nuevo LayerSetId, mismos groupIds (los colores
        // podrían seguir siendo los mismos, pero es una computación distinta).
        var regenerated = new VectorLayerSetVersion(
            ProjectId, ImageId, Version: 2, LayerSetId: Guid.NewGuid(), PaletteId, PaletteVersion: 2,
            Layers: new List<VectorLayer>
            {
                new(GroupRedId, "Rojo", "#ff0000", 60.0, false, Guid.NewGuid()),
                new(GroupGreenId, "Verde", "#00ff00", 40.0, false, Guid.NewGuid()),
            },
            SourceWidthPx: 100, SourceHeightPx: 100, CreatedAt: DateTimeOffset.UtcNow);
        vectorLayerService.AddLayerSet(regenerated);

        var current = service.FindCurrent(ProjectId, ImageId, PaletteId);

        Assert.NotNull(current);
        Assert.Equal(2, current!.Value.LayerSet.PaletteVersion);
        // Sin asignaciones para esta versión nueva: la de PaletteVersion=1 no migró.
        Assert.Null(current!.Value.Assignments);
    }
}
