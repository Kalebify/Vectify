using Microsoft.Extensions.Options;
using Vectify.Api.Contracts;
using Vectify.Api.Options;

namespace Vectify.Api.Checking;

/// <summary>
/// Valida que el sourceId del SVG de origen venga informado, que
/// sourceKind sea "vector" o "simplification", y resuelve las tolerancias
/// efectivas (valor del cliente si viene informado y en rango, o el default
/// configurado -- ver <see cref="CheckOptions"/>) antes de que el resultado
/// llegue al motor Python.
/// </summary>
public sealed class CheckParameterValidator : ICheckParameterValidator
{
    private readonly CheckOptions _options;

    public CheckParameterValidator(IOptions<CheckOptions> options)
    {
        _options = options.Value;
    }

    public CheckParameterValidationResult Validate(CheckRequest request)
    {
        if (request.SourceId == Guid.Empty)
        {
            return CheckParameterValidationResult.Failure(
                "invalid_parameters", "El sourceId del SVG de origen es requerido.");
        }

        var sourceKind = ParseSourceKind(request.SourceKind);
        if (sourceKind is null)
        {
            return CheckParameterValidationResult.Failure(
                "invalid_parameters",
                $"sourceKind '{request.SourceKind}' desconocido. Valores válidos: vector, simplification.");
        }

        var closeGapRatioResult = ResolveRatio(
            request.CloseGapRatio, _options.DefaultCloseGapRatio,
            _options.MinCloseGapRatio, _options.MaxCloseGapRatio, "closeGapRatio");
        if (closeGapRatioResult.Failure is not null)
        {
            return closeGapRatioResult.Failure;
        }

        var duplicatePointRatioResult = ResolveRatio(
            request.DuplicatePointRatio, _options.DefaultDuplicatePointRatio,
            _options.MinDuplicatePointRatio, _options.MaxDuplicatePointRatio, "duplicatePointRatio");
        if (duplicatePointRatioResult.Failure is not null)
        {
            return duplicatePointRatioResult.Failure;
        }

        return CheckParameterValidationResult.Success(new CheckParameters(
            closeGapRatioResult.Value, duplicatePointRatioResult.Value, sourceKind.Value));
    }

    private static (double Value, CheckParameterValidationResult? Failure) ResolveRatio(
        double? requested, double defaultValue, double min, double max, string fieldName)
    {
        var value = requested ?? defaultValue;
        if (double.IsNaN(value) || double.IsInfinity(value) || value <= min || value > max)
        {
            return (0, CheckParameterValidationResult.Failure(
                "invalid_parameters", $"{fieldName} debe estar en el rango ({min}, {max}]."));
        }

        return (value, null);
    }

    private static CheckSourceKind? ParseSourceKind(string? sourceKind)
    {
        if (string.IsNullOrWhiteSpace(sourceKind))
        {
            return null;
        }

        return sourceKind.Trim().ToLowerInvariant() switch
        {
            "vector" => CheckSourceKind.Vector,
            "simplification" => CheckSourceKind.Simplification,
            _ => null,
        };
    }
}
