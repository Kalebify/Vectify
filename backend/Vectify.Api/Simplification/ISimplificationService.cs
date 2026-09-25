using Vectify.Api.Contracts;

namespace Vectify.Api.Simplification;

/// <summary>
/// Orquesta la etapa de simplificación de nodos (M1-S07): validar parámetros,
/// localizar el SVG de origen (una VectorVersion YA generada, M1-S05),
/// generar un preview reversible (sin persistir) o aplicar (persistir como
/// nueva SimplificationVersion, cacheando por SVG de origen + parámetros).
/// </summary>
public interface ISimplificationService
{
    /// <summary>
    /// Genera un preview de la simplificación (nodeCount antes/después, % de
    /// reducción y el SVG resultante) SIN persistir nada -- ni en storage ni
    /// en el registro de versiones. Reversible por construcción: no hay
    /// estado que "cancelar" porque nunca se escribió ninguno.
    /// </summary>
    Task<SimplificationPreviewResult> PreviewAsync(
        Guid projectId, Guid imageId, SimplifyRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Aplica la simplificación: persiste el SVG resultante y crea una nueva
    /// SimplificationVersion (nunca sobrescribe una anterior). Mismo patrón
    /// de caché+lock+versionado que VectorizationService/ThresholdService.
    /// </summary>
    Task<SimplificationResult> ApplyAsync(
        Guid projectId, Guid imageId, SimplifyRequest request, CancellationToken cancellationToken);

    /// <summary>Recupera un registro ya aplicado (para servir sus bytes), o null si no existe.</summary>
    SimplificationVersion? FindSimplification(Guid projectId, Guid imageId, Guid simplificationId);
}
