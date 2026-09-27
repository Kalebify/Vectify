using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace Vectorify.Api.VectorLayers;

/// <summary>
/// Corrige la causa raíz confirmada de M2.1-S01 ("el pipeline multicolor
/// termina en blanco y negro"): <c>app.core.vector_engine.VtracerEngine.trace</c>
/// (compartido con el pipeline B/N de M1-S05) llama a VTracer con
/// <c>colormode="binary"</c>, que SIEMPRE emite <c>fill="#000000"</c> en
/// el/los &lt;path&gt; resultantes -- un modo sin ningún concepto de "color de
/// entrada". El <see cref="Vectorify.Api.ColorPalette.ColorGroup.ColorHex"/>
/// real sobrevive intacta la metadata/serialización (confirmado
/// independientemente antes de este fix, ver IMPL.md), pero JAMÁS se
/// aplicaba a la geometría SVG real -- por eso el panel de paleta se veía
/// bien (usa la metadata directo) y el canvas de capas (que renderiza el SVG
/// real vía &lt;img&gt;) mostraba todo negro.
///
/// Esta clase reescribe SOLO el atributo `fill` de cada &lt;path&gt; del SVG
/// YA recibido de Python -- nunca los comandos `d`, ni ningún otro atributo
/// (`transform`, `fill-rule`, etc.) -- mismo criterio de alcance acotado que
/// <see cref="Vectorify.Api.Dimensioning.SvgDimensionWriter"/> de M1-S09:
/// transformación de metadata puntual sobre el XML ya generado, en memoria,
/// determinista, sin llamar a Python de nuevo.
///
/// Deliberadamente en .NET, no en Python (ver "Ambigüedades detectadas" de
/// spec.md M2.1-S01, recomendación del orquestador): mantiene
/// <c>VtracerEngine</c>/<c>VectorizationService</c> -- compartidos con el
/// pipeline B/N -- totalmente intactos, y no toca el contrato HTTP de
/// <c>/vectorize-layers</c>. El fix queda aislado a la ruta multicolor
/// (<see cref="VectorLayerService"/>), aditivo y sin riesgo de regresión
/// sobre M1-S03/M1-S04/M1-S05 (ver <c>VectorizationServiceTests</c> y el
/// suite de <c>test_vectorize_route.py</c>, re-verificados sin cambios).
///
/// Formas con agujeros internos (<c>hierarchical="stacked"</c> de VTracer,
/// ver vector_engine.py, punto 3 de su docstring): sus subpaths anidados
/// (sentido de recorrido opuesto, fill-rule por winding) viven todos DENTRO
/// del mismo atributo `d` de un único &lt;path&gt; -- el fill se aplica una
/// sola vez por &lt;path&gt; completo, nunca por subpath individual, así que
/// la regla de winding/evenodd existente para los agujeros no se toca.
/// </summary>
public static class SvgFillWriter
{
    /// <param name="svgText">SVG crudo de la capa, ya devuelto por Python (validado como XML bien formado por <see cref="Vectorify.Api.Clients.PythonVectorLayerClient"/> antes de llegar acá).</param>
    /// <param name="colorHex">
    /// <see cref="Vectorify.Api.ColorPalette.ColorGroup.ColorHex"/> real del
    /// grupo (ej. "#ff0000", formato ya normalizado por
    /// <c>app.services.color_palette_service._bgr_to_hex</c>), aplicado tal
    /// cual como valor de `fill` -- ningún parseo/validación adicional de
    /// color acá: ya es un valor de color CSS/SVG válido por construcción.
    /// </param>
    /// <exception cref="InvalidLayerSvgException">El SVG de la capa no es XML válido o no tiene un elemento &lt;svg&gt; como raíz.</exception>
    public static string Apply(string svgText, string colorHex)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(svgText, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException ex)
        {
            throw new InvalidLayerSvgException($"El SVG de la capa no es XML válido: {ex.Message}", ex);
        }

        var root = document.Root;
        if (root is null || !string.Equals(root.Name.LocalName, "svg", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidLayerSvgException("El SVG de la capa no tiene un elemento <svg> como raíz.");
        }

        // Mismo criterio que SvgDimensionWriter: LocalName sin importar el
        // namespace default declarado en <svg> (nunca lleva prefijo). Se
        // recorren TODOS los descendientes (no solo los hijos directos de
        // <svg>): VTracer con mode="polygon" emite cada <path> directo bajo
        // <svg> (ver vector_engine.py), pero esto no asume esa estructura --
        // cualquier <path>, a cualquier profundidad, recibe el color real.
        foreach (var path in root.Descendants().Where(
            element => string.Equals(element.Name.LocalName, "path", StringComparison.OrdinalIgnoreCase)))
        {
            path.SetAttributeValue("fill", colorHex);
        }

        return document.ToString(SaveOptions.DisableFormatting);
    }
}
