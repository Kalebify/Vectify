using Microsoft.Extensions.Options;
using Vectify.Api.Contracts;
using Vectify.Api.Options;

namespace Vectify.Api.Preprocessing;

/// <summary>
/// Valida, en orden, que contraste/brillo/denoise estén dentro de los rangos
/// configurados (<see cref="PreprocessOptions"/>) y sean valores numéricos
/// finitos. Rechaza en vez de recortar ("clamp"): un valor fuera de rango es un
/// error controlado, no se silencia (ver spec.md, criterios de aceptación:
/// "Parámetros fuera de rango ... producen error controlado").
/// </summary>
public sealed class PreprocessParameterValidator : IPreprocessParameterValidator
{
    private readonly PreprocessOptions _options;

    public PreprocessParameterValidator(IOptions<PreprocessOptions> options)
    {
        _options = options.Value;
    }

    public PreprocessParameterValidationResult Validate(PreprocessRequest request)
    {
        if (double.IsNaN(request.Contrast) || double.IsInfinity(request.Contrast))
        {
            return PreprocessParameterValidationResult.Failure(
                "invalid_parameters", "El contraste debe ser un número finito.");
        }

        if (request.Contrast < _options.MinContrast || request.Contrast > _options.MaxContrast)
        {
            return PreprocessParameterValidationResult.Failure(
                "invalid_parameters",
                $"El contraste debe estar entre {_options.MinContrast} y {_options.MaxContrast}.");
        }

        if (request.Brightness < _options.MinBrightness || request.Brightness > _options.MaxBrightness)
        {
            return PreprocessParameterValidationResult.Failure(
                "invalid_parameters",
                $"El brillo debe estar entre {_options.MinBrightness} y {_options.MaxBrightness}.");
        }

        if (request.Denoise < _options.MinDenoise || request.Denoise > _options.MaxDenoise)
        {
            return PreprocessParameterValidationResult.Failure(
                "invalid_parameters",
                $"La reducción de ruido debe estar entre {_options.MinDenoise} y {_options.MaxDenoise}.");
        }

        return PreprocessParameterValidationResult.Success(
            new PreprocessParameters(request.Grayscale, request.Contrast, request.Brightness, request.Denoise));
    }
}
