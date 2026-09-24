using Microsoft.Extensions.Options;
using Vectify.Api.Contracts;
using Vectify.Api.Options;

namespace Vectify.Api.Threshold;

/// <summary>
/// Valida que el previewId de origen venga informado y que el valor de
/// umbral esté dentro del rango configurado (<see cref="ThresholdOptions"/>).
/// Rechaza en vez de recortar ("clamp"), mismo criterio que
/// PreprocessParameterValidator: un valor fuera de rango es un error
/// controlado, no se silencia.
/// </summary>
public sealed class ThresholdParameterValidator : IThresholdParameterValidator
{
    private readonly ThresholdOptions _options;

    public ThresholdParameterValidator(IOptions<ThresholdOptions> options)
    {
        _options = options.Value;
    }

    public ThresholdParameterValidationResult Validate(ThresholdRequest request)
    {
        if (request.PreviewId == Guid.Empty)
        {
            return ThresholdParameterValidationResult.Failure(
                "invalid_parameters", "El previewId del preview preprocesado de origen es requerido.");
        }

        if (request.Value < _options.MinValue || request.Value > _options.MaxValue)
        {
            return ThresholdParameterValidationResult.Failure(
                "invalid_parameters",
                $"El umbral debe estar entre {_options.MinValue} y {_options.MaxValue}.");
        }

        return ThresholdParameterValidationResult.Success(new ThresholdParameters(request.Value, request.Invert));
    }
}
