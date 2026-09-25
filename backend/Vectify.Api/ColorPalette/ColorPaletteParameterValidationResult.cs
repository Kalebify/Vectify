namespace Vectify.Api.ColorPalette;

/// <summary>Resultado de validar los parámetros de detección de paleta de colores recibidos del cliente.</summary>
public sealed class ColorPaletteParameterValidationResult
{
    public bool IsValid { get; }
    public string? ErrorCode { get; }
    public string? ErrorMessage { get; }
    public ColorPaletteParameters? Parameters { get; }

    private ColorPaletteParameterValidationResult(
        bool isValid, string? errorCode, string? errorMessage, ColorPaletteParameters? parameters)
    {
        IsValid = isValid;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        Parameters = parameters;
    }

    public static ColorPaletteParameterValidationResult Success(ColorPaletteParameters parameters) =>
        new(true, null, null, parameters);

    public static ColorPaletteParameterValidationResult Failure(string errorCode, string errorMessage) =>
        new(false, errorCode, errorMessage, null);
}
