using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace Vectify.Api.Dimensioning;

/// <summary>
/// Núcleo de M1-S09: reescribe SOLO los atributos del elemento raíz
/// &lt;svg&gt; (<c>width</c>, <c>height</c>, <c>viewBox</c> si falta,
/// <c>preserveAspectRatio</c>) para que el SVG represente un tamaño físico
/// en milímetros, SIN tocar ningún <c>&lt;path d="..."&gt;</c> -- ver
/// spec.md: "es una transformación de metadata del elemento raíz &lt;svg&gt;,
/// no de geometría interna". Puramente en memoria (XDocument), determinista
/// (mismo SVG + mismos mm -> mismos bytes siempre) y sin ningún cálculo de
/// imagen/geometría compleja -- por eso esta etapa entera vive en C#, sin
/// llamar al motor Python (ver decisión documentada en el reporte del
/// sprint).
///
/// Unidad interna documentada (spec.md, "Reglas": "Unidad interna
/// documentada"): 1 unidad de las coordenadas del SVG generado por esta
/// pipeline (viewBox, y los puntos dentro de cada `d`) equivale a 1 píxel de
/// la máscara binaria B/N (M1-S04) que VTracer vectorizó -- nunca se asume
/// ningún DPI/PPI. VectorVersion.Width/VectorVersion.Height (M1-S05) y
/// SimplificationVersion.Width/Height (M1-S07, heredados sin cambios de su
/// VectorVersion de origen) ya son ese ancho/alto en píxeles, reportados por
/// el motor Python a partir de la máscara decodificada -- son la fuente de
/// verdad de <paramref name="sourceWidthPx"/>/<paramref name="sourceHeightPx"/>,
/// no algo que se vuelva a inferir leyendo el propio XML del SVG (que, tal
/// como lo emite VTracer, no siempre incluye un `viewBox` explícito).
/// </summary>
public static class SvgDimensionWriter
{
    /// <param name="svgText">SVG de origen completo, sin modificar.</param>
    /// <param name="sourceWidthPx">Ancho del lienzo interno (VectorVersion/SimplificationVersion.Width), usado SOLO para sintetizar un `viewBox` si el SVG de origen no trae uno.</param>
    /// <param name="sourceHeightPx">Alto del lienzo interno, mismo criterio que <paramref name="sourceWidthPx"/>.</param>
    /// <param name="widthMm">Ancho físico final ya resuelto y validado (ver DimensionParameterValidator.ResolveDimensions).</param>
    /// <param name="heightMm">Alto físico final ya resuelto y validado.</param>
    /// <param name="deform">
    /// Si es true (proporción desbloqueada), fija <c>preserveAspectRatio="none"</c>
    /// para que un visor conforme a SVG estire el contenido en vez de agregar
    /// márgenes -- ver spec.md, criterios de aceptación: "esto SÍ deforma el
    /// diseño (estiramiento no uniforme), permitido explícitamente". Si es
    /// false, se quita cualquier `preserveAspectRatio` previo (el default
    /// "xMidYMid meet" no tiene efecto visible porque ancho/alto en mm ya
    /// guardan la proporción original).
    /// </param>
    /// <exception cref="InvalidDimensionSourceSvgException">El SVG de origen no es XML válido o no tiene un elemento &lt;svg&gt; como raíz.</exception>
    public static string Apply(string svgText, int sourceWidthPx, int sourceHeightPx, double widthMm, double heightMm, bool deform)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(svgText, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException ex)
        {
            throw new InvalidDimensionSourceSvgException($"El SVG de origen no es XML válido: {ex.Message}", ex);
        }

        var root = document.Root;
        if (root is null || !string.Equals(root.Name.LocalName, "svg", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDimensionSourceSvgException("El SVG de origen no tiene un elemento <svg> como raíz.");
        }

        // Los atributos width/height/viewBox/preserveAspectRatio de <svg> nunca
        // llevan prefijo de namespace (son atributos "sin namespace" del
        // estándar SVG/XML, sin importar el xmlns default del elemento) --
        // mismo criterio usado por PythonVectorizeClient/PythonSimplifyClient
        // al leer Root.Name.LocalName.
        if (root.Attribute("viewBox") is null)
        {
            root.SetAttributeValue(
                "viewBox", FormattableString.Invariant($"0 0 {sourceWidthPx} {sourceHeightPx}"));
        }

        root.SetAttributeValue("width", FormatMm(widthMm));
        root.SetAttributeValue("height", FormatMm(heightMm));

        if (deform)
        {
            root.SetAttributeValue("preserveAspectRatio", "none");
        }
        else
        {
            root.Attribute("preserveAspectRatio")?.Remove();
        }

        return document.ToString(SaveOptions.DisableFormatting);
    }

    // Precisión de 0.001mm (1 micrón): sobra para fabricación con láser y
    // evita arrastrar el ruido de punto flotante de las divisiones de
    // DimensionParameterValidator.ResolveDimensions hacia el archivo final.
    private static string FormatMm(double valueMm) =>
        valueMm.ToString("0.###", CultureInfo.InvariantCulture) + "mm";
}
