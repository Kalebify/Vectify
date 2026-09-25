using Microsoft.Extensions.Options;
using Vectify.Api.Contracts;
using Vectify.Api.Options;
using Vectify.Api.Simplification;

namespace Vectify.Api.Tests.Simplification;

/// <summary>
/// Cubre spec.md M1-S07, criterios de aceptación: "Presets de tolerancia
/// Bajo/Medio/Alto seleccionables" y "Validación de tolerancia en ASP.NET
/// Core antes de invocar el motor (rango válido, no solo el preset sino
/// también si se expone un valor numérico custom)".
/// </summary>
public sealed class SimplificationParameterValidatorTests
{
    private static SimplificationParameterValidator CreateValidator(SimplificationOptions? options = null) =>
        new(Microsoft.Extensions.Options.Options.Create(options ?? new SimplificationOptions()));

    [Fact]
    public void Validate_WhenVectorIdIsEmpty_ReturnsInvalidParameters()
    {
        var validator = CreateValidator();

        var result = validator.Validate(new SimplifyRequest(Guid.Empty, "low", null));

        Assert.False(result.IsValid);
        Assert.Equal("invalid_parameters", result.ErrorCode);
    }

    [Theory]
    [InlineData("low")]
    [InlineData("LOW")]
    [InlineData("medium")]
    [InlineData("high")]
    public void Validate_WithKnownPreset_ResolvesToConfiguredEpsilon(string preset)
    {
        var options = new SimplificationOptions { LowEpsilonRatio = 0.001, MediumEpsilonRatio = 0.005, HighEpsilonRatio = 0.02 };
        var validator = CreateValidator(options);

        var result = validator.Validate(new SimplifyRequest(Guid.NewGuid(), preset, null));

        Assert.True(result.IsValid);
        var expectedEpsilon = preset.ToLowerInvariant() switch
        {
            "low" => options.LowEpsilonRatio,
            "medium" => options.MediumEpsilonRatio,
            "high" => options.HighEpsilonRatio,
            _ => throw new InvalidOperationException(),
        };
        Assert.Equal(expectedEpsilon, result.Parameters!.EpsilonRatio);
        Assert.Equal(preset.ToLowerInvariant(), result.Parameters.Preset);
    }

    [Fact]
    public void Validate_WithUnknownPreset_ReturnsInvalidParameters()
    {
        var validator = CreateValidator();

        var result = validator.Validate(new SimplifyRequest(Guid.NewGuid(), "extreme", null));

        Assert.False(result.IsValid);
        Assert.Equal("invalid_parameters", result.ErrorCode);
    }

    [Fact]
    public void Validate_WithCustomToleranceWithinRange_ReturnsSuccessWithNullPreset()
    {
        var validator = CreateValidator();

        var result = validator.Validate(new SimplifyRequest(Guid.NewGuid(), null, 0.03));

        Assert.True(result.IsValid);
        Assert.Equal(0.03, result.Parameters!.EpsilonRatio);
        Assert.Null(result.Parameters.Preset);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.1)]
    [InlineData(0.6)]
    public void Validate_WithCustomToleranceOutOfRange_ReturnsInvalidParameters(double tolerance)
    {
        var validator = CreateValidator(new SimplificationOptions { MinCustomTolerance = 0.0, MaxCustomTolerance = 0.5 });

        var result = validator.Validate(new SimplifyRequest(Guid.NewGuid(), null, tolerance));

        Assert.False(result.IsValid);
        Assert.Equal("invalid_parameters", result.ErrorCode);
    }

    [Fact]
    public void Validate_WhenBothPresetAndToleranceAreProvided_ReturnsInvalidParameters()
    {
        var validator = CreateValidator();

        var result = validator.Validate(new SimplifyRequest(Guid.NewGuid(), "low", 0.02));

        Assert.False(result.IsValid);
        Assert.Equal("invalid_parameters", result.ErrorCode);
    }

    [Fact]
    public void Validate_WhenNeitherPresetNorToleranceAreProvided_ReturnsInvalidParameters()
    {
        var validator = CreateValidator();

        var result = validator.Validate(new SimplifyRequest(Guid.NewGuid(), null, null));

        Assert.False(result.IsValid);
        Assert.Equal("invalid_parameters", result.ErrorCode);
    }
}
