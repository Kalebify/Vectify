using Vectify.Api.Contracts;
using Vectify.Api.Preprocessing;

namespace Vectify.Api.Tests.Threshold;

/// <summary>
/// IPreprocessService en memoria para tests unitarios de ThresholdService:
/// permite registrar de antemano el/los preview(s) "ya preprocesados" que
/// FindPreview debe devolver, sin depender de PreprocessService real ni de
/// OpenCV. GeneratePreviewAsync no se usa desde ThresholdService (solo
/// FindPreview), así que lanza si se llega a invocar.
/// </summary>
internal sealed class FakePreprocessService : IPreprocessService
{
    private readonly Dictionary<(Guid ProjectId, Guid ImageId, Guid PreviewId), PreprocessConfigRecord> _previews = new();

    public void AddPreview(PreprocessConfigRecord record) =>
        _previews[(record.ProjectId, record.ImageId, record.PreviewId)] = record;

    public Task<PreprocessResult> GeneratePreviewAsync(
        Guid projectId, Guid imageId, PreprocessRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException("ThresholdService no debería llamar a GeneratePreviewAsync.");

    public PreprocessConfigRecord? FindPreview(Guid projectId, Guid imageId, Guid previewId) =>
        _previews.GetValueOrDefault((projectId, imageId, previewId));
}
