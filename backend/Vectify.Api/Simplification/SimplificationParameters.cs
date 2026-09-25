using System.Globalization;

namespace Vectify.Api.Simplification;

/// <summary>
/// Parámetros efectivos de la etapa de simplificación de nodos (M1-S07).
/// <see cref="EpsilonRatio"/> es la tolerancia de Douglas-Peucker YA resuelta
/// a un número (relativo a la diagonal del bounding box del SVG de origen,
/// nunca un valor absoluto en píxeles, para que escale con el tamaño del
/// diseño -- ver Vectify.Api.Options.SimplificationOptions y
/// SimplificationParameterValidator). <see cref="Preset"/> conserva el nombre
/// del preset elegido (low/medium/high) solo para mostrarlo de vuelta en la
/// respuesta -- es null cuando el cliente mandó una tolerancia numérica
/// custom en vez de un preset (criterio de aceptación: "no solo el preset
/// sino también si se expone un valor numérico custom").
/// </summary>
public sealed record SimplificationParameters(double EpsilonRatio, string? Preset)
{
    /// <summary>
    /// Clave estable para deduplicar/cachear simplificaciones: se basa
    /// únicamente en el epsilon efectivo (dos requests con el mismo epsilon,
    /// uno vía preset y otro vía valor custom idéntico, son la misma
    /// simplificación en términos de resultado -- Preset es solo de
    /// presentación).
    /// </summary>
    public string ToCacheKey() => $"epsilon={EpsilonRatio.ToString("F6", CultureInfo.InvariantCulture)}";
}
