using Vectify.Api.Checking;
using Vectify.Api.Contracts;
using Vectify.Api.Options;

namespace Vectify.Api.Tests.Checking;

/// <summary>
/// Pruebas unitarias de CheckParameterValidator: sourceId requerido,
/// sourceKind debe ser "vector"/"simplification", tolerancias opcionales
/// (default de configuración si se omiten) y validación de rango.
/// </summary>
public sealed class CheckParameterValidatorTests
{
    private static CheckParameterValidator CreateValidator(CheckOptions? options = null) =>
        new(Microsoft.Extensions.Options.Options.Create(options ?? new CheckOptions()));

    private static readonly Guid SourceId = Guid.NewGuid();

    [Fact]
    public void Validate_WhenSourceIdIsEmpty_ReturnsInvalidParameters()
    {
        var validator = CreateValidator();

        var result = validator.Validate(new CheckRequest("vector", Guid.Empty, null, null));

        Assert.False(result.IsValid);
        Assert.Equal("invalid_parameters", result.ErrorCode);
    }

    [Theory]
    [InlineData("vector", CheckSourceKind.Vector)]
    [InlineData("VECTOR", CheckSourceKind.Vector)]
    [InlineData(" simplification ", CheckSourceKind.Simplification)]
    public void Validate_WhenSourceKindIsKnown_ResolvesCaseInsensitively(string sourceKind, CheckSourceKind expected)
    {
        var validator = CreateValidator();

        var result = validator.Validate(new CheckRequest(sourceKind, SourceId, null, null));

        Assert.True(result.IsValid);
        Assert.Equal(expected, result.Parameters!.SourceKind);
    }

    [Fact]
    public void Validate_WhenSourceKindIsUnknown_ReturnsInvalidParameters()
    {
        var validator = CreateValidator();

        var result = validator.Validate(new CheckRequest("mask", SourceId, null, null));

        Assert.False(result.IsValid);
        Assert.Equal("invalid_parameters", result.ErrorCode);
    }

    [Fact]
    public void Validate_WhenTolerancesAreOmitted_UsesConfiguredDefaults()
    {
        var options = new CheckOptions { DefaultCloseGapRatio = 0.01, DefaultDuplicatePointRatio = 0.003 };
        var validator = CreateValidator(options);

        var result = validator.Validate(new CheckRequest("vector", SourceId, null, null));

        Assert.True(result.IsValid);
        Assert.Equal(0.01, result.Parameters!.CloseGapRatio);
        Assert.Equal(0.003, result.Parameters.DuplicatePointRatio);
    }

    [Fact]
    public void Validate_WhenTolerancesAreProvidedAndInRange_UsesRequestedValues()
    {
        var validator = CreateValidator();

        var result = validator.Validate(new CheckRequest("vector", SourceId, 0.02, 0.01));

        Assert.True(result.IsValid);
        Assert.Equal(0.02, result.Parameters!.CloseGapRatio);
        Assert.Equal(0.01, result.Parameters.DuplicatePointRatio);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.1)]
    [InlineData(0.6)]
    public void Validate_WhenCloseGapRatioIsOutOfRange_ReturnsInvalidParameters(double closeGapRatio)
    {
        var validator = CreateValidator();

        var result = validator.Validate(new CheckRequest("vector", SourceId, closeGapRatio, null));

        Assert.False(result.IsValid);
        Assert.Equal("invalid_parameters", result.ErrorCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.1)]
    [InlineData(0.6)]
    public void Validate_WhenDuplicatePointRatioIsOutOfRange_ReturnsInvalidParameters(double duplicatePointRatio)
    {
        var validator = CreateValidator();

        var result = validator.Validate(new CheckRequest("vector", SourceId, null, duplicatePointRatio));

        Assert.False(result.IsValid);
        Assert.Equal("invalid_parameters", result.ErrorCode);
    }
}
