using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Vectify.Api.Options;
using Vectify.Api.Tests.TestSupport;
using Vectify.Api.Validation;

namespace Vectify.Api.Tests.Validation;

/// <summary>
/// Pruebas unitarias de ImageUploadValidator: cubre los errores controlados del
/// spec ("Errores": formato no soportado, tamaño máximo, archivo vacío/corrupto)
/// y el camino feliz para PNG/JPG/WEBP.
/// </summary>
public sealed class ImageUploadValidatorTests
{
    private static ImageUploadValidator CreateValidator(long maxFileSizeBytes = 15 * 1024 * 1024) =>
        new(Microsoft.Extensions.Options.Options.Create(new UploadOptions
        {
            MaxFileSizeBytes = maxFileSizeBytes,
            AllowedContentTypes = "image/png,image/jpeg,image/webp",
        }));

    private static FormFile CreateFile(byte[] bytes, string fileName, string contentType)
    {
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, stream.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType,
        };
    }

    [Fact]
    public void Validate_WhenFileIsNull_ReturnsEmptyFile()
    {
        var result = CreateValidator().Validate(null);

        Assert.False(result.IsValid);
        Assert.Equal("empty_file", result.ErrorCode);
    }

    [Fact]
    public void Validate_WhenFileHasZeroBytes_ReturnsEmptyFile()
    {
        var file = CreateFile([], "vacio.png", "image/png");

        var result = CreateValidator().Validate(file);

        Assert.False(result.IsValid);
        Assert.Equal("empty_file", result.ErrorCode);
    }

    [Fact]
    public void Validate_WhenFileExceedsMaxSize_ReturnsFileTooLarge()
    {
        var file = CreateFile(SampleImages.ValidPng1x1, "grande.png", "image/png");
        var validator = CreateValidator(maxFileSizeBytes: 10);

        var result = validator.Validate(file);

        Assert.False(result.IsValid);
        Assert.Equal("file_too_large", result.ErrorCode);
    }

    [Theory]
    [InlineData("imagen.gif", "image/gif")]
    [InlineData("imagen.svg", "image/svg+xml")]
    [InlineData("imagen.txt", "text/plain")]
    public void Validate_WhenFormatIsNotSupported_ReturnsUnsupportedFormat(string fileName, string contentType)
    {
        var file = CreateFile(SampleImages.NotAnImage, fileName, contentType);

        var result = CreateValidator().Validate(file);

        Assert.False(result.IsValid);
        Assert.Equal("unsupported_format", result.ErrorCode);
    }

    [Fact]
    public void Validate_WhenExtensionDoesNotMatchDeclaredContentType_ReturnsUnsupportedFormat()
    {
        // Content-Type dice PNG pero la extensión es .jpg: no coincide con el mapeo permitido.
        var file = CreateFile(SampleImages.ValidPng1x1, "imagen.jpg", "image/png");

        var result = CreateValidator().Validate(file);

        Assert.False(result.IsValid);
        Assert.Equal("unsupported_format", result.ErrorCode);
    }

    [Fact]
    public void Validate_WhenBytesDoNotMatchDeclaredFormat_ReturnsCorruptFile()
    {
        // Extensión y Content-Type dicen PNG, pero el contenido no tiene la firma PNG.
        var file = CreateFile(SampleImages.NotAnImage, "imagen.png", "image/png");

        var result = CreateValidator().Validate(file);

        Assert.False(result.IsValid);
        Assert.Equal("corrupt_file", result.ErrorCode);
    }

    [Fact]
    public void Validate_WhenPngIsValid_ReturnsSuccess()
    {
        var file = CreateFile(SampleImages.ValidPng1x1, "imagen.png", "image/png");

        var result = CreateValidator().Validate(file);

        Assert.True(result.IsValid);
        Assert.Equal("image/png", result.ContentType);
        Assert.Equal(".png", result.Extension);
    }

    [Fact]
    public void Validate_WhenJpegIsValid_ReturnsSuccess()
    {
        var file = CreateFile(SampleImages.ValidJpegHeader32x16, "imagen.jpg", "image/jpeg");

        var result = CreateValidator().Validate(file);

        Assert.True(result.IsValid);
        Assert.Equal("image/jpeg", result.ContentType);
        Assert.Equal(".jpg", result.Extension);
    }

    [Fact]
    public void Validate_WhenWebpIsValid_ReturnsSuccess()
    {
        var file = CreateFile(SampleImages.ValidWebpVp8x100x200, "imagen.webp", "image/webp");

        var result = CreateValidator().Validate(file);

        Assert.True(result.IsValid);
        Assert.Equal("image/webp", result.ContentType);
        Assert.Equal(".webp", result.Extension);
    }

    // Defecto 1 (QA sobre M1-S02): un archivo con firma binaria válida pero
    // truncado a mitad de su contenido real (no solo el caso trivial de 8 bytes con
    // la cabecera PNG) pasaba ImageSignature.Matches y la API respondía 201. Ahora
    // ImageUploadValidator también intenta una decodificación real con ImageSharp.

    [Fact]
    public void Validate_WhenPngHasOnlyTheEightByteSignature_ReturnsCorruptFile()
    {
        // Caso exacto reportado por QA: 8 bytes, solo la firma PNG, nada más.
        byte[] onlySignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        var file = CreateFile(onlySignature, "imagen.png", "image/png");

        var result = CreateValidator().Validate(file);

        Assert.False(result.IsValid);
        Assert.Equal("corrupt_file", result.ErrorCode);
    }

    [Fact]
    public void Validate_WhenPngIsTruncatedMidContent_ReturnsCorruptFile()
    {
        var file = CreateFile(SampleImages.TruncatedPng, "imagen.png", "image/png");

        var result = CreateValidator().Validate(file);

        Assert.False(result.IsValid);
        Assert.Equal("corrupt_file", result.ErrorCode);
    }

    [Fact]
    public void Validate_WhenJpegIsTruncatedMidContent_ReturnsCorruptFile()
    {
        var file = CreateFile(SampleImages.TruncatedJpeg, "imagen.jpg", "image/jpeg");

        var result = CreateValidator().Validate(file);

        Assert.False(result.IsValid);
        Assert.Equal("corrupt_file", result.ErrorCode);
    }

    [Fact]
    public void Validate_WhenWebpIsTruncatedMidContent_ReturnsCorruptFile()
    {
        var file = CreateFile(SampleImages.TruncatedWebp, "imagen.webp", "image/webp");

        var result = CreateValidator().Validate(file);

        Assert.False(result.IsValid);
        Assert.Equal("corrupt_file", result.ErrorCode);
    }
}
