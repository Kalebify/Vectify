using Microsoft.Extensions.Options;
using Vectify.Api.Contracts;
using Vectify.Api.Options;
using Vectify.Api.Threshold;

namespace Vectify.Api.Tests.Threshold;

/// <summary>
/// Cubre los límites del valor de umbral de spec.md M1-S04: valores dentro de
/// rango se aceptan (incluidos los bordes exactos), fuera de rango se
/// rechazan con "invalid_parameters"; el previewId de origen es requerido.
/// </summary>
public sealed class ThresholdParameterValidatorTests
{
    private static readonly Guid SamplePreviewId = Guid.NewGuid();

    private static ThresholdParameterValidator CreateValidator() =>
        new(Microsoft.Extensions.Options.Options.Create(new ThresholdOptions()));

    [Theory]
    [InlineData(0)]
    [InlineData(255)]
    [InlineData(128)]
    public void Validate_WhenWithinRange_ReturnsSuccessWithEffectiveParameters(int value)
    {
        var validator = CreateValidator();
        var request = new ThresholdRequest(SamplePreviewId, value, Invert: true);

        var result = validator.Validate(request);

        Assert.True(result.IsValid);
        Assert.Equal(new ThresholdParameters(value, true), result.Parameters);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(256)]
    public void Validate_WhenValueOutOfRange_ReturnsInvalidParameters(int value)
    {
        var validator = CreateValidator();
        var request = new ThresholdRequest(SamplePreviewId, value, false);

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Equal("invalid_parameters", result.ErrorCode);
    }

    [Fact]
    public void Validate_WhenPreviewIdIsEmpty_ReturnsInvalidParameters()
    {
        var validator = CreateValidator();
        var request = new ThresholdRequest(Guid.Empty, 128, false);

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Equal("invalid_parameters", result.ErrorCode);
    }
}
