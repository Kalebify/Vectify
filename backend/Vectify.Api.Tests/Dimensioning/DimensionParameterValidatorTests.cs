using Vectify.Api.Contracts;
using Vectify.Api.Dimensioning;
using Vectify.Api.Options;

namespace Vectify.Api.Tests.Dimensioning;

/// <summary>
/// Pruebas unitarias de DimensionParameterValidator: sourceId/sourceKind
/// requeridos, la combinación ancho/alto coherente con LockAspectRatio, y --
/// a diferencia de Simplification/CheckParameterValidator -- un segundo paso
/// (ResolveDimensions) que necesita el tamaño en píxeles del SVG de origen
/// para calcular el valor faltante cuando la proporción está bloqueada.
/// </summary>
public sealed class DimensionParameterValidatorTests
{
    private static readonly Guid SourceId = Guid.NewGuid();

    private static DimensionParameterValidator CreateValidator(DimensionOptions? options = null) =>
        new(Microsoft.Extensions.Options.Options.Create(options ?? new DimensionOptions()));

    // ---- ValidateRequest ----

    [Fact]
    public void ValidateRequest_WhenSourceIdIsEmpty_ReturnsInvalidParameters()
    {
        var validator = CreateValidator();

        var result = validator.ValidateRequest(new DimensionRequest("vector", Guid.Empty, 100, null, true));

        Assert.False(result.IsValid);
        Assert.Equal("invalid_parameters", result.ErrorCode);
    }

    [Theory]
    [InlineData("vector", DimensionSourceKind.Vector)]
    [InlineData("VECTOR", DimensionSourceKind.Vector)]
    [InlineData(" simplification ", DimensionSourceKind.Simplification)]
    public void ValidateRequest_WhenSourceKindIsKnown_ResolvesCaseInsensitively(string sourceKind, DimensionSourceKind expected)
    {
        var validator = CreateValidator();

        var result = validator.ValidateRequest(new DimensionRequest(sourceKind, SourceId, 100, null, true));

        Assert.True(result.IsValid);
        Assert.Equal(expected, result.Parameters!.SourceKind);
    }

    [Fact]
    public void ValidateRequest_WhenSourceKindIsUnknown_ReturnsInvalidParameters()
    {
        var validator = CreateValidator();

        var result = validator.ValidateRequest(new DimensionRequest("mask", SourceId, 100, null, true));

        Assert.False(result.IsValid);
        Assert.Equal("invalid_parameters", result.ErrorCode);
    }

    [Fact]
    public void ValidateRequest_WhenLockAspectRatioIsOmitted_DefaultsToTrue()
    {
        var validator = CreateValidator();

        var result = validator.ValidateRequest(new DimensionRequest("vector", SourceId, 100, null, null));

        Assert.True(result.IsValid);
        Assert.True(result.Parameters!.LockAspectRatio);
    }

    [Fact]
    public void ValidateRequest_WhenLockedAndOnlyWidthProvided_IsValid()
    {
        var validator = CreateValidator();

        var result = validator.ValidateRequest(new DimensionRequest("vector", SourceId, 100, null, true));

        Assert.True(result.IsValid);
        Assert.Equal(100, result.Parameters!.WidthMm);
        Assert.Null(result.Parameters.HeightMm);
    }

    [Fact]
    public void ValidateRequest_WhenLockedAndOnlyHeightProvided_IsValid()
    {
        var validator = CreateValidator();

        var result = validator.ValidateRequest(new DimensionRequest("vector", SourceId, null, 50, true));

        Assert.True(result.IsValid);
        Assert.Equal(50, result.Parameters!.HeightMm);
    }

    [Fact]
    public void ValidateRequest_WhenLockedAndBothProvided_ReturnsInvalidParameters()
    {
        var validator = CreateValidator();

        var result = validator.ValidateRequest(new DimensionRequest("vector", SourceId, 100, 50, true));

        Assert.False(result.IsValid);
        Assert.Equal("invalid_parameters", result.ErrorCode);
    }

    [Fact]
    public void ValidateRequest_WhenLockedAndNeitherProvided_ReturnsInvalidParameters()
    {
        var validator = CreateValidator();

        var result = validator.ValidateRequest(new DimensionRequest("vector", SourceId, null, null, true));

        Assert.False(result.IsValid);
        Assert.Equal("invalid_parameters", result.ErrorCode);
    }

    [Fact]
    public void ValidateRequest_WhenUnlockedAndBothProvided_IsValid()
    {
        var validator = CreateValidator();

        var result = validator.ValidateRequest(new DimensionRequest("vector", SourceId, 100, 50, false));

        Assert.True(result.IsValid);
        Assert.Equal(100, result.Parameters!.WidthMm);
        Assert.Equal(50, result.Parameters.HeightMm);
    }

    [Theory]
    [InlineData(100.0, null)]
    [InlineData(null, 100.0)]
    public void ValidateRequest_WhenUnlockedAndOnlyOneProvided_ReturnsInvalidParameters(double? widthMm, double? heightMm)
    {
        var validator = CreateValidator();

        var result = validator.ValidateRequest(new DimensionRequest("vector", SourceId, widthMm, heightMm, false));

        Assert.False(result.IsValid);
        Assert.Equal("invalid_parameters", result.ErrorCode);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(1000.1)]
    public void ValidateRequest_WhenWidthMmIsOutOfRange_ReturnsInvalidParameters(double widthMm)
    {
        var validator = CreateValidator();

        var result = validator.ValidateRequest(new DimensionRequest("vector", SourceId, widthMm, null, true));

        Assert.False(result.IsValid);
        Assert.Equal("invalid_parameters", result.ErrorCode);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1000.0)]
    public void ValidateRequest_WhenWidthMmIsAtTheBoundary_IsValid(double widthMm)
    {
        var validator = CreateValidator();

        var result = validator.ValidateRequest(new DimensionRequest("vector", SourceId, widthMm, null, true));

        Assert.True(result.IsValid);
    }

    // ---- ResolveDimensions ----

    [Fact]
    public void ResolveDimensions_WhenLockedWithSquareSource_DerivesTheSameHeightAsWidth()
    {
        var validator = CreateValidator();
        var raw = new DimensionRequestParameters(DimensionSourceKind.Vector, 100, null, true);

        var result = validator.ResolveDimensions(raw, sourceWidthPx: 10, sourceHeightPx: 10);

        Assert.True(result.IsValid);
        Assert.Equal(100, result.Parameters!.WidthMm);
        Assert.Equal(100, result.Parameters.HeightMm);
    }

    [Fact]
    public void ResolveDimensions_WhenLockedWithWideSource_DerivesAProportionallySmallerHeight()
    {
        var validator = CreateValidator();
        // Fuente 200x50 (4:1, "muy ancho").
        var raw = new DimensionRequestParameters(DimensionSourceKind.Vector, 400, null, true);

        var result = validator.ResolveDimensions(raw, sourceWidthPx: 200, sourceHeightPx: 50);

        Assert.True(result.IsValid);
        Assert.Equal(400, result.Parameters!.WidthMm);
        Assert.Equal(100, result.Parameters.HeightMm, precision: 6);
    }

    [Fact]
    public void ResolveDimensions_WhenLockedWithTallSource_DerivesAProportionallySmallerWidthFromHeight()
    {
        var validator = CreateValidator();
        // Fuente 50x200 (1:4, "muy alto").
        var raw = new DimensionRequestParameters(DimensionSourceKind.Vector, null, 400, true);

        var result = validator.ResolveDimensions(raw, sourceWidthPx: 50, sourceHeightPx: 200);

        Assert.True(result.IsValid);
        Assert.Equal(100, result.Parameters!.WidthMm, precision: 6);
        Assert.Equal(400, result.Parameters.HeightMm);
    }

    [Fact]
    public void ResolveDimensions_WhenUnlocked_KeepsBothValuesIndependentEvenIfTheyDeformTheDesign()
    {
        var validator = CreateValidator();
        var raw = new DimensionRequestParameters(DimensionSourceKind.Vector, 500, 10, false);

        var result = validator.ResolveDimensions(raw, sourceWidthPx: 10, sourceHeightPx: 10);

        Assert.True(result.IsValid);
        Assert.Equal(500, result.Parameters!.WidthMm);
        Assert.Equal(10, result.Parameters.HeightMm);
        Assert.False(result.Parameters.LockAspectRatio);
    }

    [Fact]
    public void ResolveDimensions_WhenLockedAndDerivedHeightExceedsMax_ReturnsDimensionOutOfRange()
    {
        var options = new DimensionOptions { MinMm = 1, MaxMm = 1000 };
        var validator = CreateValidator(options);
        // Fuente extremadamente alta (1:1000, "muy alto"): pedir el ancho
        // máximo permitido (1000mm) sobre esa proporción dispara el alto
        // calculado muy por encima del máximo.
        var raw = new DimensionRequestParameters(DimensionSourceKind.Vector, 1000, null, true);

        var result = validator.ResolveDimensions(raw, sourceWidthPx: 1, sourceHeightPx: 1000);

        Assert.False(result.IsValid);
        Assert.Equal("dimension_out_of_range", result.ErrorCode);
    }

    [Fact]
    public void ResolveDimensions_WhenLockedAndDerivedHeightIsBelowMin_ReturnsDimensionOutOfRange()
    {
        var options = new DimensionOptions { MinMm = 1, MaxMm = 1000 };
        var validator = CreateValidator(options);
        // Fuente muy ancha (1000:1): el ancho mínimo permitido (1mm) deriva un
        // alto de 0.001mm, por debajo del mínimo.
        var raw = new DimensionRequestParameters(DimensionSourceKind.Vector, 1, null, true);

        var result = validator.ResolveDimensions(raw, sourceWidthPx: 1000, sourceHeightPx: 1);

        Assert.False(result.IsValid);
        Assert.Equal("dimension_out_of_range", result.ErrorCode);
    }
}
