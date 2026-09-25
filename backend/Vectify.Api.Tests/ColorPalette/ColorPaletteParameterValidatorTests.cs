using Vectify.Api.ColorPalette;
using Vectify.Api.Contracts;
using Vectify.Api.Options;

namespace Vectify.Api.Tests.ColorPalette;

/// <summary>
/// Cubre spec.md M2-S01, criterios de aceptación: "dos parámetros
/// configurables: tolerancia ... y número objetivo de colores" y la defensa
/// en profundidad de rangos antes de invocar al motor Python.
/// </summary>
public sealed class ColorPaletteParameterValidatorTests
{
    private static ColorPaletteParameterValidator CreateValidator(ColorPaletteOptions? options = null) =>
        new(Microsoft.Extensions.Options.Options.Create(options ?? new ColorPaletteOptions()));

    [Fact]
    public void Validate_WhenParametersAreOmitted_ResolvesToDefaults()
    {
        var options = new ColorPaletteOptions { DefaultTolerance = 15.0 };
        var validator = CreateValidator(options);

        var result = validator.Validate(new ColorPaletteDetectRequest(null, null, null));

        Assert.True(result.IsValid);
        Assert.Equal(15.0, result.Parameters!.Tolerance);
        Assert.Null(result.Parameters.MaxColors);
    }

    [Fact]
    public void Validate_WithExplicitToleranceAndMaxColorsWithinRange_ReturnsSuccess()
    {
        var validator = CreateValidator();

        var result = validator.Validate(new ColorPaletteDetectRequest(null, 20.0, 8));

        Assert.True(result.IsValid);
        Assert.Equal(20.0, result.Parameters!.Tolerance);
        Assert.Equal(8, result.Parameters.MaxColors);
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(100.1)]
    public void Validate_WithToleranceOutOfRange_ReturnsInvalidParameters(double tolerance)
    {
        var validator = CreateValidator(new ColorPaletteOptions { MinTolerance = 0, MaxTolerance = 100 });

        var result = validator.Validate(new ColorPaletteDetectRequest(null, tolerance, null));

        Assert.False(result.IsValid);
        Assert.Equal("invalid_parameters", result.ErrorCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65)]
    public void Validate_WithMaxColorsOutOfRange_ReturnsInvalidParameters(int maxColors)
    {
        var validator = CreateValidator(new ColorPaletteOptions { MinColors = 1, MaxColorsUpperBound = 64 });

        var result = validator.Validate(new ColorPaletteDetectRequest(null, 10.0, maxColors));

        Assert.False(result.IsValid);
        Assert.Equal("invalid_parameters", result.ErrorCode);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(64)]
    public void Validate_WithMaxColorsAtBoundaries_ReturnsSuccess(int maxColors)
    {
        var validator = CreateValidator(new ColorPaletteOptions { MinColors = 1, MaxColorsUpperBound = 64 });

        var result = validator.Validate(new ColorPaletteDetectRequest(null, 10.0, maxColors));

        Assert.True(result.IsValid);
        Assert.Equal(maxColors, result.Parameters!.MaxColors);
    }

    [Fact]
    public void Validate_WithNullMaxColors_MeansNoUpperLimitAndIsAlwaysValid()
    {
        var validator = CreateValidator();

        var result = validator.Validate(new ColorPaletteDetectRequest(null, 10.0, null));

        Assert.True(result.IsValid);
        Assert.Null(result.Parameters!.MaxColors);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Validate_WithNonFiniteTolerance_ReturnsInvalidParameters(double tolerance)
    {
        var validator = CreateValidator();

        var result = validator.Validate(new ColorPaletteDetectRequest(null, tolerance, null));

        Assert.False(result.IsValid);
        Assert.Equal("invalid_parameters", result.ErrorCode);
    }
}
