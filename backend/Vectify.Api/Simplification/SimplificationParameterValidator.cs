using Microsoft.Extensions.Options;
using Vectify.Api.Contracts;
using Vectify.Api.Options;

namespace Vectify.Api.Simplification;

/// <summary>
/// Valida que el vectorId del SVG de origen venga informado y que se haya
/// elegido exactamente una forma de tolerancia: un preset conocido
/// (low/medium/high) o un valor numérico custom dentro de rango -- nunca
/// ambos, nunca ninguno. Resuelve el preset elegido al epsilon numérico
/// configurado (ver <see cref="SimplificationOptions"/>) antes de que el
/// resultado llegue al motor Python, que nunca conoce el nombre del preset
/// (mismo criterio de encapsulamiento que app.core.vector_engine del lado
/// Python: la Web API resuelve la política de negocio, el motor solo ejecuta
/// el algoritmo con el número ya resuelto).
/// </summary>
public sealed class SimplificationParameterValidator : ISimplificationParameterValidator
{
    private readonly SimplificationOptions _options;

    public SimplificationParameterValidator(IOptions<SimplificationOptions> options)
    {
        _options = options.Value;
    }

    public SimplificationParameterValidationResult Validate(SimplifyRequest request)
    {
        if (request.VectorId == Guid.Empty)
        {
            return SimplificationParameterValidationResult.Failure(
                "invalid_parameters", "El vectorId del SVG de origen es requerido.");
        }

        var hasPreset = !string.IsNullOrWhiteSpace(request.Preset);
        var hasTolerance = request.Tolerance.HasValue;

        if (hasPreset && hasTolerance)
        {
            return SimplificationParameterValidationResult.Failure(
                "invalid_parameters",
                "Especificá un preset (low/medium/high) o una tolerancia numérica custom, no ambos.");
        }

        if (!hasPreset && !hasTolerance)
        {
            return SimplificationParameterValidationResult.Failure(
                "invalid_parameters",
                "Se requiere un preset (low/medium/high) o una tolerancia numérica custom.");
        }

        if (hasTolerance)
        {
            return ValidateCustomTolerance(request.Tolerance!.Value);
        }

        return ValidatePreset(request.Preset!);
    }

    private SimplificationParameterValidationResult ValidateCustomTolerance(double tolerance)
    {
        if (double.IsNaN(tolerance) || double.IsInfinity(tolerance)
            || tolerance <= _options.MinCustomTolerance || tolerance > _options.MaxCustomTolerance)
        {
            return SimplificationParameterValidationResult.Failure(
                "invalid_parameters",
                $"La tolerancia custom debe estar en el rango ({_options.MinCustomTolerance}, {_options.MaxCustomTolerance}].");
        }

        return SimplificationParameterValidationResult.Success(new SimplificationParameters(tolerance, Preset: null));
    }

    private SimplificationParameterValidationResult ValidatePreset(string preset)
    {
        var normalized = preset.Trim().ToLowerInvariant();
        var epsilon = normalized switch
        {
            "low" => _options.LowEpsilonRatio,
            "medium" => _options.MediumEpsilonRatio,
            "high" => _options.HighEpsilonRatio,
            _ => (double?)null,
        };

        if (epsilon is null)
        {
            return SimplificationParameterValidationResult.Failure(
                "invalid_parameters",
                $"Preset '{preset}' desconocido. Valores válidos: low, medium, high.");
        }

        return SimplificationParameterValidationResult.Success(new SimplificationParameters(epsilon.Value, normalized));
    }
}
