using Vectify.Api.Contracts;

namespace Vectify.Api.ColorPalette;

/// <summary>Valida los parámetros de detección de paleta de colores recibidos antes de llamar a Python.</summary>
public interface IColorPaletteParameterValidator
{
    ColorPaletteParameterValidationResult Validate(ColorPaletteDetectRequest request);
}
