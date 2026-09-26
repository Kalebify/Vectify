using Vectify.Api.Components;

namespace Vectify.Api.Tests.Components;

/// <summary>
/// IComponentAnalysisService en memoria para tests unitarios de
/// ComponentGroupService: permite registrar de antemano el/los
/// ComponentSetVersion "ya calculados" que FindLatest debe devolver, sin
/// depender de Python/storage/vectorización reales -- ComponentGroupService
/// SOLO llama a FindLatest (nunca a AnalyzeAsync), así que alcanza con este
/// doble liviano. Mismo criterio que Tests.Components.FakeVectorizationService.
/// </summary>
internal sealed class FakeComponentAnalysisService : IComponentAnalysisService
{
    private readonly Dictionary<(Guid ProjectId, Guid ImageId, Guid VectorId), ComponentSetVersion> _sets = new();

    public void AddComponentSet(ComponentSetVersion record) =>
        _sets[(record.ProjectId, record.ImageId, record.VectorId)] = record;

    public Task<ComponentSetResult> AnalyzeAsync(Guid projectId, Guid imageId, Guid vectorId, CancellationToken cancellationToken) =>
        throw new NotSupportedException("ComponentGroupService no debería llamar a AnalyzeAsync.");

    public ComponentSetVersion? FindLatest(Guid projectId, Guid imageId, Guid vectorId) =>
        _sets.GetValueOrDefault((projectId, imageId, vectorId));
}
