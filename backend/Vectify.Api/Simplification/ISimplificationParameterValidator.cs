using Vectify.Api.Contracts;

namespace Vectify.Api.Simplification;

/// <summary>Valida los parámetros de simplificación recibidos antes de llamar a Python.</summary>
public interface ISimplificationParameterValidator
{
    SimplificationParameterValidationResult Validate(SimplifyRequest request);
}
