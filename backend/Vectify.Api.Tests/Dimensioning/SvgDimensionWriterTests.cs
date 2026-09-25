using System.Globalization;
using System.Xml.Linq;
using Vectify.Api.Dimensioning;

namespace Vectify.Api.Tests.Dimensioning;

/// <summary>
/// Pruebas unitarias de SvgDimensionWriter: la única pieza que reescribe
/// bytes de SVG en M1-S09. Puramente en memoria, sin storage ni HTTP --
/// cubre directamente las "Pruebas" obligatorias de spec.md: aspect ratios
/// distintos (1:1, muy ancho, muy alto), round-trip, y verificación de
/// medidas (viewBox y width/height en mm matemáticamente consistentes).
/// </summary>
public sealed class SvgDimensionWriterTests
{
    private const string SquareSvgWithoutViewBox =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\"><path d=\"M2,2 L8,2 L8,8 L2,8 Z\"/></svg>";

    [Fact]
    public void Apply_WhenSourceHasNoViewBox_SynthesizesOneFromSourcePixelDimensions()
    {
        var result = SvgDimensionWriter.Apply(SquareSvgWithoutViewBox, 10, 10, 100, 100, deform: false);

        var root = XDocument.Parse(result).Root!;
        Assert.Equal("0 0 10 10", root.Attribute("viewBox")!.Value);
    }

    [Fact]
    public void Apply_NeverTouchesThePathData()
    {
        var result = SvgDimensionWriter.Apply(SquareSvgWithoutViewBox, 10, 10, 100, 50, deform: true);

        var path = XDocument.Parse(result).Root!.Elements().Single(e => e.Name.LocalName == "path");
        Assert.Equal("M2,2 L8,2 L8,8 L2,8 Z", path.Attribute("d")!.Value);
    }

    [Fact]
    public void Apply_WritesWidthAndHeightWithExplicitMmUnit()
    {
        var result = SvgDimensionWriter.Apply(SquareSvgWithoutViewBox, 10, 10, 150, 75.5, deform: false);

        var root = XDocument.Parse(result).Root!;
        Assert.Equal("150mm", root.Attribute("width")!.Value);
        Assert.Equal("75.5mm", root.Attribute("height")!.Value);
    }

    [Fact]
    public void Apply_WhenLocked_DoesNotSetPreserveAspectRatio()
    {
        var result = SvgDimensionWriter.Apply(SquareSvgWithoutViewBox, 10, 10, 100, 100, deform: false);

        var root = XDocument.Parse(result).Root!;
        Assert.Null(root.Attribute("preserveAspectRatio"));
    }

    [Fact]
    public void Apply_WhenUnlocked_SetsPreserveAspectRatioNoneToAllowDeforming()
    {
        var result = SvgDimensionWriter.Apply(SquareSvgWithoutViewBox, 10, 10, 300, 40, deform: true);

        var root = XDocument.Parse(result).Root!;
        Assert.Equal("none", root.Attribute("preserveAspectRatio")!.Value);
    }

    [Fact]
    public void Apply_WhenSourceAlreadyHasAPreserveAspectRatio_RemovesItWhenNotDeforming()
    {
        const string svgWithPreserve =
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\" preserveAspectRatio=\"none\"><path d=\"M0,0 L10,0 L10,10 L0,10 Z\"/></svg>";

        var result = SvgDimensionWriter.Apply(svgWithPreserve, 10, 10, 20, 20, deform: false);

        var root = XDocument.Parse(result).Root!;
        Assert.Null(root.Attribute("preserveAspectRatio"));
    }

    [Fact]
    public void Apply_WhenSourceAlreadyHasAViewBox_KeepsItUnchanged()
    {
        const string svgWithViewBox =
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 20 30\" width=\"20\" height=\"30\"><path d=\"M0,0 L20,0 L20,30 L0,30 Z\"/></svg>";

        var result = SvgDimensionWriter.Apply(svgWithViewBox, 20, 30, 200, 300, deform: false);

        var root = XDocument.Parse(result).Root!;
        Assert.Equal("0 0 20 30", root.Attribute("viewBox")!.Value);
    }

    [Theory]
    [InlineData(10, 10, 50, 50)] // 1:1
    [InlineData(200, 20, 500, 50)] // muy ancho
    [InlineData(20, 200, 50, 500)] // muy alto
    public void Apply_ForDifferentAspectRatios_ViewBoxAndMmStayMathematicallyConsistent(
        int sourceWidthPx, int sourceHeightPx, double widthMm, double heightMm)
    {
        var svg = $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{sourceWidthPx}\" height=\"{sourceHeightPx}\"><path d=\"M0,0 L1,1\"/></svg>";

        var result = SvgDimensionWriter.Apply(svg, sourceWidthPx, sourceHeightPx, widthMm, heightMm, deform: false);

        var root = XDocument.Parse(result).Root!;
        var viewBoxParts = root.Attribute("viewBox")!.Value.Split(' ');
        var viewBoxWidth = double.Parse(viewBoxParts[2], CultureInfo.InvariantCulture);
        var viewBoxHeight = double.Parse(viewBoxParts[3], CultureInfo.InvariantCulture);

        // El aspect ratio mm:mm debe coincidir con el aspect ratio del viewBox
        // (en este set de casos el caller siempre pasó dimensiones que
        // preservan la proporción original -- ver DimensionParameterValidator).
        var mmAspect = widthMm / heightMm;
        var viewBoxAspect = viewBoxWidth / viewBoxHeight;
        Assert.Equal(mmAspect, viewBoxAspect, precision: 6);

        Assert.Equal(sourceWidthPx.ToString(CultureInfo.InvariantCulture), viewBoxParts[2]);
        Assert.Equal(sourceHeightPx.ToString(CultureInfo.InvariantCulture), viewBoxParts[3]);
    }

    [Fact]
    public void Apply_RoundTrip_ReparsingTheOutputYieldsTheSamePhysicalDimensions()
    {
        var first = SvgDimensionWriter.Apply(SquareSvgWithoutViewBox, 10, 10, 123.456, 78.9, deform: false);

        // Simula "exportar y volver a abrir": re-parsear el SVG YA dimensionado
        // y volver a aplicarle EXACTAMENTE las mismas dimensiones debe producir
        // bytes idénticos (determinismo) y seguir representando el mismo
        // tamaño físico -- ver spec.md, "Pruebas": "round-trip".
        var root = XDocument.Parse(first).Root!;
        Assert.Equal("123.456mm", root.Attribute("width")!.Value);
        Assert.Equal("78.9mm", root.Attribute("height")!.Value);

        var second = SvgDimensionWriter.Apply(first, 10, 10, 123.456, 78.9, deform: false);
        Assert.Equal(first, second);
    }

    [Fact]
    public void Apply_IsDeterministic_SameInputProducesByteIdenticalOutput()
    {
        var first = SvgDimensionWriter.Apply(SquareSvgWithoutViewBox, 10, 10, 42, 42, deform: false);
        var second = SvgDimensionWriter.Apply(SquareSvgWithoutViewBox, 10, 10, 42, 42, deform: false);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Apply_WhenSourceIsNotWellFormedXml_ThrowsInvalidDimensionSourceSvgException()
    {
        Assert.Throws<InvalidDimensionSourceSvgException>(
            () => SvgDimensionWriter.Apply("<svg><unclosed></svg>", 10, 10, 10, 10, deform: false));
    }

    [Fact]
    public void Apply_WhenRootElementIsNotSvg_ThrowsInvalidDimensionSourceSvgException()
    {
        Assert.Throws<InvalidDimensionSourceSvgException>(
            () => SvgDimensionWriter.Apply("<notasvg></notasvg>", 10, 10, 10, 10, deform: false));
    }
}
