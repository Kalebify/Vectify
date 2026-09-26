using Vectify.Api.Contracts;
using Vectify.Api.Vectorization;

namespace Vectify.Api.Tests.Components;

/// <summary>
/// IVectorizationService en memoria para tests unitarios de
/// ComponentAnalysisService: permite registrar de antemano el/los
/// VectorVersion "ya generados" que FindVector debe devolver, sin depender
/// de VectorizationService real ni de VTracer. GenerateVectorAsync no se usa
/// desde ComponentAnalysisService (solo FindVector), así que lanza si se
/// llega a invocar -- mismo criterio que Dimensioning.Tests.FakeVectorizationService.
/// </summary>
internal sealed class FakeVectorizationService : IVectorizationService
{
    private readonly Dictionary<(Guid ProjectId, Guid ImageId, Guid VectorId), VectorVersion> _vectors = new();

    public void AddVector(VectorVersion record) =>
        _vectors[(record.ProjectId, record.ImageId, record.VectorId)] = record;

    public Task<VectorResult> GenerateVectorAsync(
        Guid projectId, Guid imageId, VectorizeRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException("ComponentAnalysisService no debería llamar a GenerateVectorAsync.");

    public VectorVersion? FindVector(Guid projectId, Guid imageId, Guid vectorId) =>
        _vectors.GetValueOrDefault((projectId, imageId, vectorId));
}
