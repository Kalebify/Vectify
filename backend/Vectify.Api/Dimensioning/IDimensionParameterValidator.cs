using Vectify.Api.Contracts;

namespace Vectify.Api.Dimensioning;

/// <summary>
/// Valida los parámetros de dimensiones físicas en dos pasos, a diferencia
/// de ISimplificationParameterValidator/ICheckParameterValidator (un solo
/// `Validate`): el valor faltante en modo bloqueado depende del aspect ratio
/// del SVG de origen, que <see cref="DimensionService"/> recién conoce
/// después de localizarlo (ver <see cref="DimensionRequestParameters"/>).
/// </summary>
public interface IDimensionParameterValidator
{
    /// <summary>
    /// Primer paso: valida la forma de la solicitud sin conocer el SVG de
    /// origen (sourceId/sourceKind presentes y conocidos, la combinación
    /// ancho/alto es coherente con LockAspectRatio, cada valor informado está
    /// en rango).
    /// </summary>
    DimensionRequestValidationResult ValidateRequest(DimensionRequest request);

    /// <summary>
    /// Segundo paso: dado el tamaño en píxeles del SVG de origen YA
    /// localizado, calcula el valor faltante (si la proporción está
    /// bloqueada) y valida en rango el resultado final -- un ancho/alto
    /// informado válido puede producir un valor CALCULADO fuera de rango
    /// (ej. un aspect ratio muy extremo), caso que este paso también rechaza.
    /// </summary>
    DimensionParameterValidationResult ResolveDimensions(
        DimensionRequestParameters raw, int sourceWidthPx, int sourceHeightPx);
}
