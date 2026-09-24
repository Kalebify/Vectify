using Vectify.Api.Contracts;

namespace Vectify.Api.Preprocessing;

/// <summary>Orquesta validar parámetros, cachear/generar y recuperar previews de preprocesamiento.</summary>
public interface IPreprocessService
{
    Task<PreprocessResult> GeneratePreviewAsync(
        Guid projectId, Guid imageId, PreprocessRequest request, CancellationToken cancellationToken);

    /// <summary>Recupera un registro ya generado (para servir sus bytes), o null si no existe.</summary>
    PreprocessConfigRecord? FindPreview(Guid projectId, Guid imageId, Guid previewId);
}
