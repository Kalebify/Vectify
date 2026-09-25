using Vectify.Api.Contracts;

namespace Vectify.Api.Checking;

/// <summary>Valida sourceId/sourceKind y resuelve las tolerancias del Laser Checker antes de llamar a Python.</summary>
public interface ICheckParameterValidator
{
    CheckParameterValidationResult Validate(CheckRequest request);
}
