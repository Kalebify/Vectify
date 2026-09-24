using Vectify.Api.Contracts;

namespace Vectify.Api.Preprocessing;

/// <summary>Valida los parámetros de preprocesamiento recibidos antes de llamar a Python.</summary>
public interface IPreprocessParameterValidator
{
    PreprocessParameterValidationResult Validate(PreprocessRequest request);
}
