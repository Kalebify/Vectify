using Vectify.Api.Contracts;
using Vectify.Api.Vectorization;

namespace Vectify.Api.Tests.Vectorization;

/// <summary>
/// Cubre el único requisito de spec.md M1-S05 sobre parámetros: el maskId de
/// la máscara B/N de origen es requerido. No hay parámetros numéricos que
/// validar en este sprint (ver VectorParameters).
/// </summary>
public sealed class VectorParameterValidatorTests
{
    [Fact]
    public void Validate_WhenMaskIdIsProvided_ReturnsSuccess()
    {
        var validator = new VectorParameterValidator();
        var request = new VectorizeRequest(Guid.NewGuid());

        var result = validator.Validate(request);

        Assert.True(result.IsValid);
        Assert.NotNull(result.Parameters);
    }

    [Fact]
    public void Validate_WhenMaskIdIsEmpty_ReturnsInvalidParameters()
    {
        var validator = new VectorParameterValidator();
        var request = new VectorizeRequest(Guid.Empty);

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Equal("invalid_parameters", result.ErrorCode);
    }
}
