using System.Globalization;

namespace Vectify.Api.Threshold;

/// <summary>
/// Parámetros efectivos (ya validados, dentro de rango) de la etapa de
/// threshold: valor de umbral global (0-255) e inversión blanco/negro. Ver
/// spec.md M1-S04, sección "Usuario podrá". El modo adaptativo (vs. global) se
/// decidió NO incluir en este sprint -- ver "Decisiones de diseño" en el
/// reporte del sprint.
/// </summary>
public sealed record ThresholdParameters(int Value, bool Invert)
{
    /// <summary>
    /// Clave estable para deduplicar/cachear máscaras: dos solicitudes con el
    /// mismo valor de umbral e inversión producen la misma clave. No incluye
    /// el previewId de origen -- eso lo agrega el caller (ver
    /// ThresholdService), porque la misma combinación de parámetros sobre dos
    /// previews distintos son máscaras distintas.
    /// </summary>
    public string ToCacheKey() => string.Create(
        CultureInfo.InvariantCulture, $"v={Value}|inv={Invert}");
}
