/**
 * Inyecta un resaltado visual sobre uno o más `<path>` de un SVG (por
 * índice de documento, 0-based -- el mismo `pathIndex` que devuelve el
 * Laser Checker, M1-S08) para click-to-highlight sobre VectorCanvas
 * (M1-S06). Trabaja SIEMPRE sobre texto (DOMParser/XMLSerializer en
 * memoria, nunca `dangerouslySetInnerHTML` ni el SVG adjunto al DOM real):
 * el resultado se convierte a data URL (ver svgToDataUrl) y se pasa como
 * `src` de un `<img>`, mismo criterio de defensa en profundidad que el
 * resto del visualizador -- el navegador nunca ejecuta el SVG como
 * documento vivo.
 *
 * El resaltado se logra agregando atributos de PRESENTACIÓN directos
 * (`stroke`, `stroke-width`, `fill`, `fill-opacity`) al elemento, no una
 * clase CSS + `<style>`: VectorCanvas renderiza el SVG como `<img>`, que no
 * aplica hojas de estilo externas ni honra `<style>` inyectado después
 * (además, `<style>` es uno de los elementos que la sanitización del lado
 * Python elimina por completo -- ver app.core.svg_processing). Los
 * atributos de presentación directos sí son parte del propio SVG y se
 * respetan al rasterizarlo.
 *
 * Nunca lanza: si el SVG no parsea, o el índice pedido no existe, devuelve
 * el SVG de entrada sin modificar (fail-safe -- preferible a romper el
 * render del canvas por un resaltado que no se pudo aplicar).
 */

const HIGHLIGHT_STROKE = "#ff2d55";
const HIGHLIGHT_FILL = "#ff2d55";
const HIGHLIGHT_FILL_OPACITY = "0.35";
/** Fracción del lado más grande del SVG usada como ancho de trazo del resaltado -- ver docstring: no hay una unidad "correcta" única, así que se deriva del tamaño del propio diseño (mismo criterio de tolerancia relativa que el resto de M1-S08). */
const HIGHLIGHT_STROKE_WIDTH_RATIO = 0.01;

export function highlightSvgPath(svgText: string, pathIndices: number[]): string {
  if (pathIndices.length === 0) {
    return svgText;
  }

  let doc: Document;
  try {
    doc = new DOMParser().parseFromString(svgText, "image/svg+xml");
  } catch {
    return svgText;
  }

  if (doc.getElementsByTagName("parsererror").length > 0) {
    return svgText;
  }

  const root = doc.documentElement;
  const width = Number.parseFloat(root.getAttribute("width") ?? "0");
  const height = Number.parseFloat(root.getAttribute("height") ?? "0");
  const referenceSize = Math.max(width, height, 1);
  const strokeWidth = (referenceSize * HIGHLIGHT_STROKE_WIDTH_RATIO).toFixed(3);

  const paths = doc.getElementsByTagName("path");
  let appliedAny = false;

  for (const pathIndex of pathIndices) {
    const target = paths.item(pathIndex);
    if (!target) {
      continue;
    }
    target.setAttribute("stroke", HIGHLIGHT_STROKE);
    target.setAttribute("stroke-width", strokeWidth);
    target.setAttribute("fill", HIGHLIGHT_FILL);
    target.setAttribute("fill-opacity", HIGHLIGHT_FILL_OPACITY);
    appliedAny = true;
  }

  if (!appliedAny) {
    return svgText;
  }

  return new XMLSerializer().serializeToString(doc);
}
