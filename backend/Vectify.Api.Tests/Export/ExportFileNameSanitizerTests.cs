using Vectify.Api.Export;

namespace Vectify.Api.Tests.Export;

/// <summary>
/// Pruebas unitarias de la función pura ExportFileNameSanitizer (M1-S10):
/// cubre el caso de prueba obligatorio de spec.md "nombres de archivo con
/// caracteres especiales" -- tildes, espacios, símbolos reservados de
/// Windows, separadores de ruta, caracteres de control, nombre vacío/nulo,
/// y nombres reservados del sistema (CON/PRN/etc.).
/// </summary>
public sealed class ExportFileNameSanitizerTests
{
    [Fact]
    public void SanitizeBaseName_PreservesAccentsSpacesAndBenignSymbols()
    {
        var result = ExportFileNameSanitizer.SanitizeBaseName("diseño final (v2) #1.png");

        Assert.Equal("diseño final (v2) #1", result);
    }

    [Theory]
    [InlineData("a<b>c.png", "a_b_c")]
    [InlineData("a:b.png", "a_b")]
    [InlineData("\"quoted\".png", "_quoted_")]
    [InlineData("a|b?c*d.png", "a_b_c_d")]
    public void SanitizeBaseName_ReplacesWindowsReservedCharsWithUnderscore(string original, string expected)
    {
        var result = ExportFileNameSanitizer.SanitizeBaseName(original);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void SanitizeBaseName_StripsAnyDirectoryComponent()
    {
        var forward = ExportFileNameSanitizer.SanitizeBaseName("some/dir/logo.png");
        var backward = ExportFileNameSanitizer.SanitizeBaseName(@"some\dir\logo.png");

        Assert.Equal("logo", forward);
        Assert.Equal("logo", backward);
    }

    [Fact]
    public void SanitizeBaseName_StripsOnlyTheLastExtension()
    {
        var result = ExportFileNameSanitizer.SanitizeBaseName("archive.tar.gz");

        Assert.Equal("archive.tar", result);
    }

    [Fact]
    public void SanitizeBaseName_ReplacesControlCharactersWithUnderscore()
    {
        var result = ExportFileNameSanitizer.SanitizeBaseName("logo\r\n\t.png");

        Assert.Equal("logo___", result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SanitizeBaseName_WhenOriginalIsMissing_FallsBackToDefaultName(string? original)
    {
        var result = ExportFileNameSanitizer.SanitizeBaseName(original);

        Assert.Equal(ExportFileNameSanitizer.FallbackBaseName, result);
    }

    [Fact]
    public void SanitizeBaseName_WhenSanitizedResultIsEmpty_FallsBackToDefaultName()
    {
        // Antes de la extensión solo quedan espacios: ni son un carácter
        // reservado (no se reemplazan por "_") ni sobreviven al recorte de
        // bordes, así que el resultado final queda vacío.
        var result = ExportFileNameSanitizer.SanitizeBaseName("   .png");

        Assert.Equal(ExportFileNameSanitizer.FallbackBaseName, result);
    }

    [Fact]
    public void SanitizeBaseName_TrimsTrailingDotsAndSpaces()
    {
        var result = ExportFileNameSanitizer.SanitizeBaseName("logo final . .png");

        Assert.False(result.EndsWith('.'));
        Assert.False(result.EndsWith(' '));
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("PRN")]
    [InlineData("COM1")]
    [InlineData("LPT9")]
    public void SanitizeBaseName_WhenResultIsAReservedWindowsName_PrefixesAnUnderscore(string reservedName)
    {
        var result = ExportFileNameSanitizer.SanitizeBaseName($"{reservedName}.png");

        Assert.Equal($"_{reservedName}", result);
    }

    [Fact]
    public void SanitizeBaseName_WhenOriginalIsVeryLong_TruncatesToAReasonableLength()
    {
        var longName = new string('a', 500) + ".png";

        var result = ExportFileNameSanitizer.SanitizeBaseName(longName);

        Assert.True(result.Length <= 80);
        Assert.False(result.EndsWith('_'));
    }

    [Fact]
    public void SanitizeBaseName_WhenNoExtensionPresent_KeepsTheWholeName()
    {
        var result = ExportFileNameSanitizer.SanitizeBaseName("sin_extension");

        Assert.Equal("sin_extension", result);
    }

    [Fact]
    public void Build_ComposesBaseNameStageSuffixAndSvgExtension()
    {
        var result = ExportFileNameSanitizer.Build("mi diseño.png", "vector-v1");

        Assert.Equal("mi diseño-vector-v1.svg", result);
    }

    [Fact]
    public void Build_WhenOriginalFileNameIsNull_UsesFallbackBaseName()
    {
        var result = ExportFileNameSanitizer.Build(null, "dimension-v2");

        Assert.Equal($"{ExportFileNameSanitizer.FallbackBaseName}-dimension-v2.svg", result);
    }

    [Fact]
    public void Build_IsDeterministic_SameInputsProduceTheSameOutput()
    {
        var first = ExportFileNameSanitizer.Build("Diseño Láser #7.png", "simplification-v3");
        var second = ExportFileNameSanitizer.Build("Diseño Láser #7.png", "simplification-v3");

        Assert.Equal(first, second);
    }
}
