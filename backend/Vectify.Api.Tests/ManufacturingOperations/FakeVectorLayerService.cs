using Vectify.Api.VectorLayers;

namespace Vectify.Api.Tests.ManufacturingOperations;

/// <summary>
/// IVectorLayerService en memoria para tests unitarios de
/// ManufacturingOperationService: permite registrar de antemano el/los
/// VectorLayerSetVersion "ya generados" que FindLatest debe devolver, sin
/// depender de Python/storage/paleta reales -- ManufacturingOperationService
/// SOLO llama a FindLatest (nunca a GenerateLayersAsync). Mismo criterio que
/// Tests.Components.FakeComponentAnalysisService.
/// </summary>
internal sealed class FakeVectorLayerService : IVectorLayerService
{
    private readonly Dictionary<(Guid ProjectId, Guid ImageId, Guid PaletteId), VectorLayerSetVersion> _sets = new();

    public void AddLayerSet(VectorLayerSetVersion record) =>
        _sets[(record.ProjectId, record.ImageId, record.PaletteId)] = record;

    public Task<VectorLayerSetResult> GenerateLayersAsync(
        Guid projectId, Guid imageId, Guid paletteId, CancellationToken cancellationToken) =>
        throw new NotSupportedException("ManufacturingOperationService no debería llamar a GenerateLayersAsync.");

    public VectorLayerSetVersion? FindLatest(Guid projectId, Guid imageId, Guid paletteId) =>
        _sets.GetValueOrDefault((projectId, imageId, paletteId));
}
