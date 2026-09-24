using Microsoft.Extensions.Options;
using Vectify.Api.Contracts;
using Vectify.Api.Options;
using Vectify.Api.Preprocessing;

namespace Vectify.Api.Tests.Preprocessing;

/// <summary>
/// Cubre "límites de sliders" del spec.md: valores dentro de rango se aceptan
/// (incluidos los bordes exactos), fuera de rango se rechazan con
/// "invalid_parameters".
/// </summary>
public sealed class PreprocessParameterValidatorTests
{
    private static PreprocessParameterValidator CreateValidator() =>
        new(Microsoft.Extensions.Options.Options.Create(new PreprocessOptions()));

    [Theory]
    [InlineData(0.5, -100, 0)]
    [InlineData(3.0, 100, 10)]
    [InlineData(1.0, 0, 5)]
    public void Validate_WhenWithinRange_ReturnsSuccessWithEffectiveParameters(double contrast, int brightness, int denoise)
    {
        var validator = CreateValidator();
        var request = new PreprocessRequest(Grayscale: true, contrast, brightness, denoise);

        var result = validator.Validate(request);

        Assert.True(result.IsValid);
        Assert.Equal(new PreprocessParameters(true, contrast, brightness, denoise), result.Parameters);
    }

    [Theory]
    [InlineData(0.49)]
    [InlineData(3.01)]
    public void Validate_WhenContrastOutOfRange_ReturnsInvalidParameters(double contrast)
    {
        var validator = CreateValidator();
        var request = new PreprocessRequest(false, contrast, 0, 0);

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Equal("invalid_parameters", result.ErrorCode);
    }

    [Theory]
    [InlineData(-101)]
    [InlineData(101)]
    public void Validate_WhenBrightnessOutOfRange_ReturnsInvalidParameters(int brightness)
    {
        var validator = CreateValidator();
        var request = new PreprocessRequest(false, 1.0, brightness, 0);

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Equal("invalid_parameters", result.ErrorCode);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(11)]
    public void Validate_WhenDenoiseOutOfRange_ReturnsInvalidParameters(int denoise)
    {
        var validator = CreateValidator();
        var request = new PreprocessRequest(false, 1.0, 0, denoise);

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Equal("invalid_parameters", result.ErrorCode);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Validate_WhenContrastIsNotFinite_ReturnsInvalidParameters(double contrast)
    {
        var validator = CreateValidator();
        var request = new PreprocessRequest(false, contrast, 0, 0);

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Equal("invalid_parameters", result.ErrorCode);
    }
}
