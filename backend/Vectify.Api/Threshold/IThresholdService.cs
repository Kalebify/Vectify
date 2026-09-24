using Vectify.Api.Contracts;

namespace Vectify.Api.Threshold;

/// <summary>Orquesta validar parámetros, localizar el preview de origen, cachear/generar y recuperar máscaras de threshold.</summary>
public interface IThresholdService
{
    Task<ThresholdResult> GenerateMaskAsync(
        Guid projectId, Guid imageId, ThresholdRequest request, CancellationToken cancellationToken);

    /// <summary>Recupera un registro ya generado (para servir sus bytes), o null si no existe.</summary>
    ThresholdConfigRecord? FindMask(Guid projectId, Guid imageId, Guid maskId);
}
