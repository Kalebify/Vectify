namespace Vectorify.Api.Assets;

/// <summary>Valida un IFormFile candidato a Asset (M2.2-S04) antes de guardarlo.</summary>
public interface IAssetUploadValidator
{
    AssetValidationResult Validate(IFormFile? file);
}
