using System.Xml.Linq;
using Vectorify.Api.VectorLayers;

namespace Vectorify.Api.Tests.VectorLayers;

/// <summary>
/// Pruebas unitarias de SvgFillWriter: la pieza que corrige la causa raíz
/// confirmada de M2.1-S01 ("el pipeline multicolor termina en blanco y
/// negro") -- inyecta el ColorHex real de un ColorGroup como `fill` de cada
/// &lt;path&gt; del SVG que devuelve VtracerEngine (siempre negro en
/// colormode="binary"). Puramente en memoria, sin storage ni HTTP -- mismo
/// criterio que SvgDimensionWriterTests (M1-S09).
/// </summary>
public sealed class SvgFillWriterTests
{
    private const string SingleBlackPathSvg =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\">" +
        "<path d=\"M2,2 L8,2 L8,8 L2,8 Z\" fill=\"#000000\" transform=\"translate(10,10)\" /></svg>";

    [Fact]
    public void Apply_ReplacesTheEnginesFixedBlackFillWithTheRealColor()
    {
        var result = SvgFillWriter.Apply(SingleBlackPathSvg, "#ff0000");

        var path = XDocument.Parse(result).Root!.Elements().Single(e => e.Name.LocalName == "path");
        Assert.Equal("#ff0000", path.Attribute("fill")!.Value);
    }

    [Fact]
    public void Apply_NeverTouchesThePathDCommand()
    {
        var result = SvgFillWriter.Apply(SingleBlackPathSvg, "#ff0000");

        var path = XDocument.Parse(result).Root!.Elements().Single(e => e.Name.LocalName == "path");
        Assert.Equal("M2,2 L8,2 L8,8 L2,8 Z", path.Attribute("d")!.Value);
    }

    [Fact]
    public void Apply_PreservesOtherAttributesLikeTransform()
    {
        var result = SvgFillWriter.Apply(SingleBlackPathSvg, "#ff0000");

        var path = XDocument.Parse(result).Root!.Elements().Single(e => e.Name.LocalName == "path");
        Assert.Equal("translate(10,10)", path.Attribute("transform")!.Value);
    }

    [Fact]
    public void Apply_WhenSvgHasNoFillAttributeAtAll_StillAddsTheRealColor()
    {
        const string svgWithoutFill =
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\"><path d=\"M2,2 L8,2 L8,8 L2,8 Z\" /></svg>";

        var result = SvgFillWriter.Apply(svgWithoutFill, "#00c800");

        var path = XDocument.Parse(result).Root!.Elements().Single(e => e.Name.LocalName == "path");
        Assert.Equal("#00c800", path.Attribute("fill")!.Value);
    }

    [Fact]
    public void Apply_WhenTheSvgHasMultiplePaths_PaintsEachOneWithTheSameColor()
    {
        const string svgWithTwoPaths =
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"20\" height=\"10\">" +
            "<path d=\"M0,0 L5,0 L5,5 L0,5 Z\" fill=\"#000000\" />" +
            "<path d=\"M10,0 L15,0 L15,5 L10,5 Z\" fill=\"#000000\" />" +
            "</svg>";

        var result = SvgFillWriter.Apply(svgWithTwoPaths, "#0000ff");

        var paths = XDocument.Parse(result).Root!.Elements().Where(e => e.Name.LocalName == "path").ToList();
        Assert.Equal(2, paths.Count);
        Assert.All(paths, p => Assert.Equal("#0000ff", p.Attribute("fill")!.Value));
    }

    [Fact]
    public void Apply_WhenAPathHasNestedHoleSubpathsInTheSameDAttribute_PaintsTheWholePathOnceKeepingTheFillRule()
    {
        // hierarchical="stacked" de VTracer: los agujeros son subpaths
        // anidados de sentido opuesto DENTRO del mismo `d` -- nunca paths
        // separados. El fill se aplica una sola vez, al <path> completo,
        // sin tocar (ni duplicar) nada del `d` ni de `fill-rule`.
        const string ringSvg =
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"20\" height=\"20\">" +
            "<path d=\"M0,0 L20,0 L20,20 L0,20 Z M5,5 L5,15 L15,15 L15,5 Z\" fill=\"#000000\" fill-rule=\"evenodd\" />" +
            "</svg>";

        var result = SvgFillWriter.Apply(ringSvg, "#ffdc00");

        var path = XDocument.Parse(result).Root!.Elements().Single(e => e.Name.LocalName == "path");
        Assert.Equal("#ffdc00", path.Attribute("fill")!.Value);
        Assert.Equal("evenodd", path.Attribute("fill-rule")!.Value);
        Assert.Equal("M0,0 L20,0 L20,20 L0,20 Z M5,5 L5,15 L15,15 L15,5 Z", path.Attribute("d")!.Value);
    }

    [Fact]
    public void Apply_WhenSvgIsNotWellFormedXml_ThrowsInvalidLayerSvgException()
    {
        const string malformed = "<svg><path d=\"M0,0 L1,1\"></svg>";

        Assert.Throws<InvalidLayerSvgException>(() => SvgFillWriter.Apply(malformed, "#ff0000"));
    }

    [Fact]
    public void Apply_WhenRootElementIsNotSvg_ThrowsInvalidLayerSvgException()
    {
        const string notAnSvg = "<root><path d=\"M0,0 L1,1\" /></root>";

        Assert.Throws<InvalidLayerSvgException>(() => SvgFillWriter.Apply(notAnSvg, "#ff0000"));
    }

    [Fact]
    public void Apply_WhenThereAreNoPathsAtAll_ReturnsTheSvgUnchangedWithoutThrowing()
    {
        const string emptySvg = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\"></svg>";

        var result = SvgFillWriter.Apply(emptySvg, "#ff0000");

        Assert.Empty(XDocument.Parse(result).Root!.Elements());
    }
}
