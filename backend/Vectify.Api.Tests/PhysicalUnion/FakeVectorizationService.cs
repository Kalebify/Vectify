using Vectify.Api.Contracts;
using Vectify.Api.Vectorization;

namespace Vectify.Api.Tests.PhysicalUnion;

/// <summary>
/// IVectorizationService en memoria para tests unitarios de
/// PhysicalUnionService: permite registrar de antemano el VectorVersion "ya
/// generado" que FindVector debe devolver, sin depender de VTracer real.
/// Mismo criterio que Tests.Components.FakeVectorizationService.
/// </summary>
internal sealed class FakeVectorizationService : IVectorizationService
{
    private readonly Dictionary<(Guid ProjectId, Guid ImageId, Guid VectorId), VectorVersion> _vectors = new();

    public void AddVector(VectorVersion record) =>
        _vectors[(record.ProjectId, record.ImageId, record.VectorId)] = record;

    public Task<VectorResult> GenerateVectorAsync(
        Guid projectId, Guid imageId, VectorizeRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException("PhysicalUnionService no debería llamar a GenerateVectorAsync.");

    public VectorVersion? FindVector(Guid projectId, Guid imageId, Guid vectorId) =>
        _vectors.GetValueOrDefault((projectId, imageId, vectorId));
}
