using Vectify.Api.Contracts;

namespace Vectify.Api.Dimensioning;

/// <summary>
/// Orquesta la etapa de dimensiones físicas en mm (M1-S09): validar
/// parámetros, localizar el SVG de origen (una VectorVersion YA generada,
/// M1-S05, o una SimplificationVersion YA generada, M1-S07 -- según
/// request.SourceKind) y aplicar (persistir) el cambio de metadata SIN
/// llamar a ningún motor externo -- pura reescritura de atributos del
/// elemento raíz &lt;svg&gt; (ver <see cref="SvgDimensionWriter"/>).
///
/// SIN PreviewAsync a propósito: a diferencia de Simplification, la
/// operación es aritmética simple de escala (no hay ningún algoritmo ni
/// llamada a un motor externo cuyo resultado no se pueda anticipar), así que
/// el preview del tamaño final se calcula 100% en el cliente (React) antes
/// de pedir "Aplicar" -- ver decisión documentada en el reporte del sprint.
/// </summary>
public interface IDimensionService
{
    /// <summary>
    /// Aplica las dimensiones físicas: persiste el SVG resultante y crea una
    /// nueva DimensionVersion (nunca sobrescribe una anterior). Mismo patrón
    /// de caché+lock+versionado que SimplificationService, salvo que no hay
    /// ninguna llamada HTTP a Python de por medio.
    /// </summary>
    Task<DimensionResult> ApplyAsync(
        Guid projectId, Guid imageId, DimensionRequest request, CancellationToken cancellationToken);

    /// <summary>Recupera un registro ya aplicado (para servir sus bytes), o null si no existe.</summary>
    DimensionVersion? FindDimension(Guid projectId, Guid imageId, Guid dimensionId);
}
