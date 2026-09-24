using System.Globalization;

namespace Vectify.Api.Preprocessing;

/// <summary>
/// Parámetros efectivos (ya validados, dentro de rango) del pipeline de
/// preprocesamiento: escala de grises, contraste, brillo y suavizado/reducción
/// de ruido. Ver spec.md M1-S03, sección "Usuario podrá".
/// </summary>
public sealed record PreprocessParameters(bool Grayscale, double Contrast, int Brightness, int Denoise)
{
    /// <summary>
    /// Clave estable para deduplicar/cachear previews: dos solicitudes con los
    /// mismos parámetros efectivos producen la misma clave, sin importar el
    /// orden en que llegaron. Contrast se redondea a 3 decimales para evitar
    /// que ruido de punto flotante insignificante invalide el cache.
    /// </summary>
    public string ToCacheKey() => string.Create(
        CultureInfo.InvariantCulture,
        $"gs={Grayscale}|c={Math.Round(Contrast, 3)}|b={Brightness}|d={Denoise}");
}
