using Vectify.Api.Contracts;

namespace Vectify.Api.Vectorization;

/// <summary>
/// Valida que el maskId de la máscara B/N de origen venga informado. No hay
/// ningún otro parámetro que validar en este sprint (ver VectorParameters).
/// Mismo criterio de "rechazar en vez de recortar" que
/// ThresholdParameterValidator.
/// </summary>
public sealed class VectorParameterValidator : IVectorParameterValidator
{
    public VectorParameterValidationResult Validate(VectorizeRequest request)
    {
        if (request.MaskId == Guid.Empty)
        {
            return VectorParameterValidationResult.Failure(
                "invalid_parameters", "El maskId de la máscara B/N de origen es requerido.");
        }

        return VectorParameterValidationResult.Success(new VectorParameters());
    }
}
