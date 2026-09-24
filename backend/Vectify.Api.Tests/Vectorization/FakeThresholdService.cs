using Vectify.Api.Contracts;
using Vectify.Api.Threshold;

namespace Vectify.Api.Tests.Vectorization;

/// <summary>
/// IThresholdService en memoria para tests unitarios de VectorizationService:
/// permite registrar de antemano la(s) máscara(s) "ya generadas" que
/// FindMask debe devolver, sin depender de ThresholdService real ni de
/// OpenCV. GenerateMaskAsync no se usa desde VectorizationService (solo
/// FindMask), así que lanza si se llega a invocar.
/// </summary>
internal sealed class FakeThresholdService : IThresholdService
{
    private readonly Dictionary<(Guid ProjectId, Guid ImageId, Guid MaskId), ThresholdConfigRecord> _masks = new();

    public void AddMask(ThresholdConfigRecord record) =>
        _masks[(record.ProjectId, record.ImageId, record.MaskId)] = record;

    public Task<ThresholdResult> GenerateMaskAsync(
        Guid projectId, Guid imageId, ThresholdRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException("VectorizationService no debería llamar a GenerateMaskAsync.");

    public ThresholdConfigRecord? FindMask(Guid projectId, Guid imageId, Guid maskId) =>
        _masks.GetValueOrDefault((projectId, imageId, maskId));
}
