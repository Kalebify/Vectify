using System.Globalization;

namespace Vectify.Api.Dimensioning;

/// <summary>
/// Dimensiones físicas efectivas YA resueltas (si la proporción estaba
/// bloqueada, el valor faltante ya se calculó a partir del aspect ratio del
/// SVG de origen -- ver <see cref="IDimensionParameterValidator.ResolveDimensions"/>)
/// y validadas contra el rango permitido. <see cref="LockAspectRatio"/> se
/// conserva (no solo para mostrarlo de vuelta) porque determina si el SVG
/// resultante lleva <c>preserveAspectRatio="none"</c> -- ver
/// <see cref="SvgDimensionWriter"/>: con la proporción bloqueada, ancho/alto en
/// mm ya guardan la misma proporción que el viewBox, así que el
/// comportamiter default (que preserva aspecto) no tiene ningún efecto
/// visible; con la proporción desbloqueada, el usuario pidió explícitamente
/// poder deformar el diseño, y sin <c>preserveAspectRatio="none"</c> un
/// visor SVG conforme al estándar centraría el contenido y dejaría espacio
/// vacío en vez de estirarlo (comportamiento default: "xMidYMid meet").
/// </summary>
public sealed record DimensionParameters(double WidthMm, double HeightMm, bool LockAspectRatio)
{
    /// <summary>
    /// Clave estable para deduplicar/cachear: incluye LockAspectRatio además
    /// de los valores finales en mm porque, aunque dos requests con el mismo
    /// ancho/alto final pero distinto LockAspectRatio puedan producir bytes
    /// visualmente equivalentes cuando el aspecto coincide, no son la misma
    /// solicitud (uno pidió explícitamente permitir deformación, el otro no)
    /// -- se prefiere no conflar ambos casos bajo la misma clave de caché.
    /// </summary>
    public string ToCacheKey() => string.Create(CultureInfo.InvariantCulture, $"width={WidthMm:F6}|height={HeightMm:F6}|lock={LockAspectRatio}");
}
