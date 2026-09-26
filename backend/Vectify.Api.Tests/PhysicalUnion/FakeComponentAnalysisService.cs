using Vectify.Api.Components;

namespace Vectify.Api.Tests.PhysicalUnion;

/// <summary>
/// IComponentAnalysisService en memoria para tests unitarios de
/// PhysicalUnionService: permite registrar de antemano el ComponentSetVersion
/// "ya calculado" que FindLatest debe devolver -- PhysicalUnionService solo
/// llama a FindLatest (nunca a AnalyzeAsync). Mismo criterio que
/// Tests.Components.FakeComponentAnalysisService.
/// </summary>
internal sealed class FakeComponentAnalysisService : IComponentAnalysisService
{
    private readonly Dictionary<(Guid ProjectId, Guid ImageId, Guid VectorId), ComponentSetVersion> _sets = new();

    public void AddComponentSet(ComponentSetVersion record) =>
        _sets[(record.ProjectId, record.ImageId, record.VectorId)] = record;

    public Task<ComponentSetResult> AnalyzeAsync(Guid projectId, Guid imageId, Guid vectorId, CancellationToken cancellationToken) =>
        throw new NotSupportedException("PhysicalUnionService no debería llamar a AnalyzeAsync.");

    public ComponentSetVersion? FindLatest(Guid projectId, Guid imageId, Guid vectorId) =>
        _sets.GetValueOrDefault((projectId, imageId, vectorId));
}
