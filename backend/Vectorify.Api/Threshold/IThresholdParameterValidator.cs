using Vectorify.Api.Contracts;

namespace Vectorify.Api.Threshold;

/// <summary>Valida los parámetros de threshold recibidos antes de llamar a Python.</summary>
public interface IThresholdParameterValidator
{
    ThresholdParameterValidationResult Validate(ThresholdRequest request);
}
