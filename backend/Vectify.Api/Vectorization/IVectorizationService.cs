using Vectify.Api.Contracts;

namespace Vectify.Api.Vectorization;

/// <summary>Orquesta validar parámetros, localizar la máscara de origen, cachear/generar y recuperar vectorizaciones.</summary>
public interface IVectorizationService
{
    Task<VectorResult> GenerateVectorAsync(
        Guid projectId, Guid imageId, VectorizeRequest request, CancellationToken cancellationToken);

    /// <summary>Recupera un registro ya generado (para servir sus bytes), o null si no existe.</summary>
    VectorVersion? FindVector(Guid projectId, Guid imageId, Guid vectorId);
}
