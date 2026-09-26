using Vectorify.Api.Contracts;

namespace Vectorify.Api.Vectorization;

/// <summary>Valida los parámetros de vectorización recibidos antes de llamar a Python.</summary>
public interface IVectorParameterValidator
{
    VectorParameterValidationResult Validate(VectorizeRequest request);
}
