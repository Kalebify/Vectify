import type { SpikeDocument, SpikeLayer } from "../types";

/**
 * Parsea un SVG fixture (multicolor-input.svg o medium-complexity.svg) a
 * SpikeDocument, leyendo los data-* de cada <g> de capa (ver types.ts).
 * Es el ÚNICO lugar donde se interpreta el SVG crudo — los 3 candidatos
 * consumen la misma estructura ya parseada, para que "importar SVG" se
 * mida de forma comparable (parseo de DOM aparte del costo de construir
 * la escena de cada motor).
 */
export function parseSpikeSvg(svgText: string): SpikeDocument {
  const doc = new DOMParser().parseFromString(svgText, "image/svg+xml");
  const errorNode = doc.querySelector("parsererror");
  if (errorNode) {
    throw new Error(`SVG inválido: ${errorNode.textContent}`);
  }
  const root = doc.documentElement;
  const width = Number(root.getAttribute("width")) || 0;
  const height = Number(root.getAttribute("height")) || 0;
  const viewBox = root.getAttribute("viewBox") ?? `0 0 ${width} ${height}`;

  const groups = Array.from(root.querySelectorAll(":scope > g"));
  const layers: SpikeLayer[] = groups.map((g, index) => {
    const paths = Array.from(g.querySelectorAll(":scope > path")).map((p) => ({
      d: p.getAttribute("d") ?? "",
      transform: p.getAttribute("transform"),
    }));
    return {
      id: g.getAttribute("id") ?? `layer-${index}`,
      groupId: g.getAttribute("data-group-id") ?? `group-${index}`,
      name: g.getAttribute("data-name") ?? `Capa ${index + 1}`,
      colorHex: g.getAttribute("data-color-hex") ?? g.getAttribute("fill") ?? "#000000",
      vectorId: g.getAttribute("data-vector-id") ?? `vector-${index}`,
      zIndex: Number(g.getAttribute("data-z-index") ?? index),
      visible: g.getAttribute("data-visible") !== "false",
      groupTransform: g.getAttribute("transform"),
      paths,
    };
  });

  return { width, height, viewBox, layers };
}

export function countNodes(doc: SpikeDocument): number {
  let count = 0;
  for (const layer of doc.layers) {
    for (const path of layer.paths) {
      // Aproxima "nodos" contando comandos de path SVG (M/L/C/Q/A/Z, mayus o minus).
      const matches = path.d.match(/[MLCQAZmlcqaz]/g);
      count += matches ? matches.length : 0;
    }
  }
  return count;
}

export async function fetchSpikeSvgText(url: string): Promise<string> {
  const res = await fetch(url);
  if (!res.ok) {
    throw new Error(`No se pudo cargar ${url}: HTTP ${res.status}`);
  }
  return res.text();
}
