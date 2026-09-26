using Vectorify.Api.Contracts;

namespace Vectorify.Api.Preprocessing;

/// <summary>Valida los parámetros de preprocesamiento recibidos antes de llamar a Python.</summary>
public interface IPreprocessParameterValidator
{
    PreprocessParameterValidationResult Validate(PreprocessRequest request);
}
