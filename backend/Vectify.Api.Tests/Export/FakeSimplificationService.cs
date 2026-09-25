using Vectify.Api.Contracts;
using Vectify.Api.Simplification;

namespace Vectify.Api.Tests.Export;

/// <summary>
/// ISimplificationService en memoria para tests unitarios de ExportService:
/// permite registrar de antemano la/las SimplificationVersion "ya aplicadas"
/// que FindSimplification debe devolver. PreviewAsync/ApplyAsync no se usan
/// desde ExportService (solo FindSimplification), así que lanzan si se
/// llegan a invocar.
/// </summary>
internal sealed class FakeSimplificationService : ISimplificationService
{
    private readonly Dictionary<(Guid ProjectId, Guid ImageId, Guid SimplificationId), SimplificationVersion> _simplifications = new();

    public void AddSimplification(SimplificationVersion record) =>
        _simplifications[(record.ProjectId, record.ImageId, record.SimplificationId)] = record;

    public Task<SimplificationPreviewResult> PreviewAsync(
        Guid projectId, Guid imageId, SimplifyRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException("ExportService no debería llamar a PreviewAsync.");

    public Task<SimplificationResult> ApplyAsync(
        Guid projectId, Guid imageId, SimplifyRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException("ExportService no debería llamar a ApplyAsync.");

    public SimplificationVersion? FindSimplification(Guid projectId, Guid imageId, Guid simplificationId) =>
        _simplifications.GetValueOrDefault((projectId, imageId, simplificationId));
}
