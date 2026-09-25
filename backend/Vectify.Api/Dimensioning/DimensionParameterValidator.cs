using Microsoft.Extensions.Options;
using Vectify.Api.Contracts;
using Vectify.Api.Options;

namespace Vectify.Api.Dimensioning;

/// <summary>
/// Implementación de <see cref="IDimensionParameterValidator"/>. Ver esa
/// interfaz para por qué la validación ocurre en dos pasos.
/// </summary>
public sealed class DimensionParameterValidator : IDimensionParameterValidator
{
    private readonly DimensionOptions _options;

    public DimensionParameterValidator(IOptions<DimensionOptions> options)
    {
        _options = options.Value;
    }

    public DimensionRequestValidationResult ValidateRequest(DimensionRequest request)
    {
        if (request.SourceId == Guid.Empty)
        {
            return DimensionRequestValidationResult.Failure(
                "invalid_parameters", "El sourceId del SVG de origen es requerido.");
        }

        var sourceKind = ParseSourceKind(request.SourceKind);
        if (sourceKind is null)
        {
            return DimensionRequestValidationResult.Failure(
                "invalid_parameters",
                $"sourceKind '{request.SourceKind}' desconocido. Valores válidos: vector, simplification.");
        }

        // Bloqueada por default (spec.md: "con lock (default)... el otro valor
        // se calcula automáticamente"), consistente con lo que expone React.
        var lockAspectRatio = request.LockAspectRatio ?? true;

        if (lockAspectRatio)
        {
            var hasWidth = request.WidthMm.HasValue;
            var hasHeight = request.HeightMm.HasValue;
            if (hasWidth == hasHeight)
            {
                return DimensionRequestValidationResult.Failure(
                    "invalid_parameters",
                    "Con la proporción bloqueada, especificá el ancho O el alto en mm (exactamente uno de los dos, no ambos ni ninguno).");
            }
        }
        else if (!request.WidthMm.HasValue || !request.HeightMm.HasValue)
        {
            return DimensionRequestValidationResult.Failure(
                "invalid_parameters",
                "Con la proporción desbloqueada, especificá el ancho Y el alto en mm (ambos son independientes).");
        }

        if (request.WidthMm.HasValue && !IsInRange(request.WidthMm.Value))
        {
            return DimensionRequestValidationResult.Failure(
                "invalid_parameters", $"widthMm debe estar en el rango [{_options.MinMm}, {_options.MaxMm}].");
        }

        if (request.HeightMm.HasValue && !IsInRange(request.HeightMm.Value))
        {
            return DimensionRequestValidationResult.Failure(
                "invalid_parameters", $"heightMm debe estar en el rango [{_options.MinMm}, {_options.MaxMm}].");
        }

        return DimensionRequestValidationResult.Success(
            new DimensionRequestParameters(sourceKind.Value, request.WidthMm, request.HeightMm, lockAspectRatio));
    }

    public DimensionParameterValidationResult ResolveDimensions(
        DimensionRequestParameters raw, int sourceWidthPx, int sourceHeightPx)
    {
        double widthMm;
        double heightMm;

        if (raw.LockAspectRatio)
        {
            // sourceWidthPx/sourceHeightPx > 0 siempre (VectorVersion/SimplificationVersion
            // solo existen para máscaras con dimensiones positivas, ver M1-S02/M1-S05).
            var aspectWidthOverHeight = sourceWidthPx / (double)sourceHeightPx;

            if (raw.WidthMm.HasValue)
            {
                widthMm = raw.WidthMm.Value;
                heightMm = widthMm / aspectWidthOverHeight;
            }
            else
            {
                heightMm = raw.HeightMm!.Value;
                widthMm = heightMm * aspectWidthOverHeight;
            }
        }
        else
        {
            widthMm = raw.WidthMm!.Value;
            heightMm = raw.HeightMm!.Value;
        }

        if (!IsInRange(widthMm))
        {
            return DimensionParameterValidationResult.Failure(
                "dimension_out_of_range",
                raw.LockAspectRatio && !raw.WidthMm.HasValue
                    ? $"El ancho calculado automáticamente a partir del alto y la proporción original ({widthMm:F3}mm) queda fuera del rango permitido [{_options.MinMm}, {_options.MaxMm}]. Probá con otro valor de alto."
                    : $"widthMm debe estar en el rango [{_options.MinMm}, {_options.MaxMm}].");
        }

        if (!IsInRange(heightMm))
        {
            return DimensionParameterValidationResult.Failure(
                "dimension_out_of_range",
                raw.LockAspectRatio && !raw.HeightMm.HasValue
                    ? $"El alto calculado automáticamente a partir del ancho y la proporción original ({heightMm:F3}mm) queda fuera del rango permitido [{_options.MinMm}, {_options.MaxMm}]. Probá con otro valor de ancho."
                    : $"heightMm debe estar en el rango [{_options.MinMm}, {_options.MaxMm}].");
        }

        return DimensionParameterValidationResult.Success(new DimensionParameters(widthMm, heightMm, raw.LockAspectRatio));
    }

    private bool IsInRange(double valueMm) =>
        !double.IsNaN(valueMm) && !double.IsInfinity(valueMm) && valueMm >= _options.MinMm && valueMm <= _options.MaxMm;

    private static DimensionSourceKind? ParseSourceKind(string? sourceKind)
    {
        if (string.IsNullOrWhiteSpace(sourceKind))
        {
            return null;
        }

        return sourceKind.Trim().ToLowerInvariant() switch
        {
            "vector" => DimensionSourceKind.Vector,
            "simplification" => DimensionSourceKind.Simplification,
            _ => null,
        };
    }
}
