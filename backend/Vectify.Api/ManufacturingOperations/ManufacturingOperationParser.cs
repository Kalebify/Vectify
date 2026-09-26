namespace Vectify.Api.ManufacturingOperations;

/// <summary>
/// Traduce el valor de wire (JSON, string case-insensitive) al enum interno
/// <see cref="ManufacturingOperationKind"/> -- mismo criterio exacto que
/// Vectify.Api.Dimensioning.DimensionParameterValidator.ParseSourceKind /
/// Vectify.Api.Checking.CheckParameterValidator (contratos HTTP usan string,
/// nunca el enum serializado por System.Text.Json). Solo los 3 valores del
/// enum son aceptados como ENTRADA -- "unassigned" nunca es algo que el
/// cliente pueda enviar, ver <see cref="ManufacturingOperationKind"/>.
/// </summary>
public static class ManufacturingOperationParser
{
    public const string UnassignedWireValue = "unassigned";

    /// <summary>Parsea un valor de entrada (POST); null si es desconocido, vacío o es "unassigned" (no es un valor asignable).</summary>
    public static ManufacturingOperationKind? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "cut" => ManufacturingOperationKind.Cut,
            "engrave" => ManufacturingOperationKind.Engrave,
            "ignore" => ManufacturingOperationKind.Ignore,
            _ => null,
        };
    }

    /// <summary>Serializa el enum interno al valor de wire (salida en respuestas).</summary>
    public static string ToWireValue(ManufacturingOperationKind kind) => kind switch
    {
        ManufacturingOperationKind.Cut => "cut",
        ManufacturingOperationKind.Engrave => "engrave",
        ManufacturingOperationKind.Ignore => "ignore",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Valor de ManufacturingOperationKind desconocido."),
    };
}
