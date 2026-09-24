namespace Vectify.Api.Validation;

/// <summary>Valida un IFormFile candidato a original de proyecto antes de guardarlo.</summary>
public interface IImageUploadValidator
{
    ImageValidationResult Validate(IFormFile? file);
}
