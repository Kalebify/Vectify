using Microsoft.Extensions.Options;
using Vectify.Api.Contracts;
using Vectify.Api.Options;

namespace Vectify.Api.ColorPalette;

/// <summary>
/// Valida tolerancia/número objetivo de colores dentro de los rangos
/// configurados (<see cref="ColorPaletteOptions"/>), resolviendo los
/// defaults cuando el cliente los omite -- mismo criterio de defensa en
/// profundidad que SimplificationParameterValidator/CheckParameterValidator
/// (Python vuelve a validar del lado suyo, pero Vectify.Api nunca confía
/// ciegamente en lo que llega del navegador).
/// </summary>
public sealed class ColorPaletteParameterValidator : IColorPaletteParameterValidator
{
    private readonly ColorPaletteOptions _options;

    public ColorPaletteParameterValidator(IOptions<ColorPaletteOptions> options)
    {
        _options = options.Value;
    }

    public ColorPaletteParameterValidationResult Validate(ColorPaletteDetectRequest request)
    {
        var tolerance = request.Tolerance ?? _options.DefaultTolerance;

        if (double.IsNaN(tolerance) || double.IsInfinity(tolerance)
            || tolerance < _options.MinTolerance || tolerance > _options.MaxTolerance)
        {
            return ColorPaletteParameterValidationResult.Failure(
                "invalid_parameters",
                $"La tolerancia debe estar en el rango [{_options.MinTolerance}, {_options.MaxTolerance}].");
        }

        if (request.MaxColors.HasValue
            && (request.MaxColors.Value < _options.MinColors || request.MaxColors.Value > _options.MaxColorsUpperBound))
        {
            return ColorPaletteParameterValidationResult.Failure(
                "invalid_parameters",
                $"maxColors debe estar en el rango [{_options.MinColors}, {_options.MaxColorsUpperBound}], o null para sin límite.");
        }

        return ColorPaletteParameterValidationResult.Success(new ColorPaletteParameters(tolerance, request.MaxColors));
    }
}
