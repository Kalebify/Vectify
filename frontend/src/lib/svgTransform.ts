/**
 * Parseo de `<path d="..." transform="...">` de un SVG de capa (M1-S05/M2-S02)
 * a una forma que VectorCanvas (M2.1-S06, Konva/react-konva) puede dibujar
 * directamente: Konva NO tiene un importador de SVG nativo (ver ADR de
 * M2.1-S05, `.sprint/.../81ad.../IMPL.md`, sección 4, fila 1/9) -- el patrón
 * documentado ahí (y ejercitado en el spike, `KonvaSpike.tsx`/
 * `shared/loadLayersFromSvg.ts`, descartable) es reusar un parser de texto
 * compartido en vez de que el motor gráfico "importe" nada. Este archivo es
 * la versión de PRODUCCIÓN de ese patrón (no el código del spike en sí).
 *
 * Las capas reales que emite el pipeline (ver
 * `backend/Vectorify.Api/App_Data/uploads/**\/vector-layers/**\/*.svg`) son
 * planas -- `<svg><path d="..." fill="#hex" transform="translate(x,y)" /></svg>`,
 * sin `<g>` anidados -- pero el parser soporta la lista completa de
 * funciones de `transform` de SVG (`translate`, `scale`, `rotate`, `skewX`,
 * `skewY`, `matrix`) por si el motor de trazado empieza a emitir algo más
 * complejo, y compone el transform de cualquier `<g>` ancestro con el del
 * propio `<path>` (orden SVG estándar: el ancestro se aplica "afuera").
 */

export interface AffineMatrix {
  a: number;
  b: number;
  c: number;
  d: number;
  e: number;
  f: number;
}

export const IDENTITY_MATRIX: AffineMatrix = { a: 1, b: 0, c: 0, d: 1, e: 0, f: 0 };

/** `A * B`: el punto se transforma primero por B, después por A (orden de composición de SVG: el primer transform de la lista es el más "externo"). */
export function multiplyMatrices(matrixA: AffineMatrix, matrixB: AffineMatrix): AffineMatrix {
  return {
    a: matrixA.a * matrixB.a + matrixA.c * matrixB.b,
    b: matrixA.b * matrixB.a + matrixA.d * matrixB.b,
    c: matrixA.a * matrixB.c + matrixA.c * matrixB.d,
    d: matrixA.b * matrixB.c + matrixA.d * matrixB.d,
    e: matrixA.a * matrixB.e + matrixA.c * matrixB.f + matrixA.e,
    f: matrixA.b * matrixB.e + matrixA.d * matrixB.f + matrixA.f,
  };
}

const TRANSFORM_FUNCTION_RE = /(\w+)\s*\(([^)]*)\)/g;

function toNumbers(argsText: string): number[] {
  return argsText
    .trim()
    .split(/[\s,]+/)
    .filter((token) => token.length > 0)
    .map(Number);
}

function degreesToRadians(degrees: number): number {
  return (degrees * Math.PI) / 180;
}

/**
 * Parsea el VALOR de un atributo `transform` (posiblemente varias funciones
 * encadenadas, ej. `"translate(5,10) rotate(45)"`) a una única matriz
 * compuesta. Nunca lanza: una función desconocida o argumentos inválidos se
 * ignoran (fail-safe, mismo criterio que `highlightSvgPath.ts`) en vez de
 * romper el render de toda la capa por un `transform` que no se pudo leer.
 */
export function parseSvgTransformAttribute(transformAttr: string | null | undefined): AffineMatrix {
  if (!transformAttr) {
    return IDENTITY_MATRIX;
  }

  let combined = IDENTITY_MATRIX;
  let match: RegExpExecArray | null;
  TRANSFORM_FUNCTION_RE.lastIndex = 0;

  while ((match = TRANSFORM_FUNCTION_RE.exec(transformAttr)) !== null) {
    const [, name, argsText] = match;
    const args = toNumbers(argsText);
    const next = functionToMatrix(name.toLowerCase(), args);
    if (next) {
      combined = multiplyMatrices(combined, next);
    }
  }

  return combined;
}

function functionToMatrix(name: string, args: number[]): AffineMatrix | null {
  switch (name) {
    case "translate": {
      const [tx = 0, ty = 0] = args;
      return { a: 1, b: 0, c: 0, d: 1, e: tx, f: ty };
    }
    case "scale": {
      const [sx, sy = sx] = args;
      if (sx === undefined) return null;
      return { a: sx, b: 0, c: 0, d: sy, e: 0, f: 0 };
    }
    case "rotate": {
      const [angleDeg, cx = 0, cy = 0] = args;
      if (angleDeg === undefined) return null;
      const rad = degreesToRadians(angleDeg);
      const cos = Math.cos(rad);
      const sin = Math.sin(rad);
      const rotation: AffineMatrix = { a: cos, b: sin, c: -sin, d: cos, e: 0, f: 0 };
      if (cx === 0 && cy === 0) {
        return rotation;
      }
      // rotate(angle, cx, cy) == translate(cx,cy) rotate(angle) translate(-cx,-cy)
      const toCenter: AffineMatrix = { a: 1, b: 0, c: 0, d: 1, e: cx, f: cy };
      const fromCenter: AffineMatrix = { a: 1, b: 0, c: 0, d: 1, e: -cx, f: -cy };
      return multiplyMatrices(multiplyMatrices(toCenter, rotation), fromCenter);
    }
    case "skewx": {
      const [angleDeg] = args;
      if (angleDeg === undefined) return null;
      return { a: 1, b: 0, c: Math.tan(degreesToRadians(angleDeg)), d: 1, e: 0, f: 0 };
    }
    case "skewy": {
      const [angleDeg] = args;
      if (angleDeg === undefined) return null;
      return { a: 1, b: Math.tan(degreesToRadians(angleDeg)), c: 0, d: 1, e: 0, f: 0 };
    }
    case "matrix": {
      const [a, b, c, d, e, f] = args;
      if ([a, b, c, d, e, f].some((n) => n === undefined || Number.isNaN(n))) return null;
      return { a, b, c, d, e, f };
    }
    default:
      return null;
  }
}

/** Parámetros de nodo Konva equivalentes a una matriz afín (mismo criterio de decomposición estándar de CSS/SVG matrix -> translate/rotate/scale/skew). */
export interface KonvaTransformProps {
  x: number;
  y: number;
  rotation: number;
  scaleX: number;
  scaleY: number;
  skewX: number;
}

/**
 * Descompone una matriz afín en los props que Konva.Node acepta
 * directamente (`x`/`y`/`rotation` en grados/`scaleX`/`scaleY`/`skewX`),
 * evitando pasarle una matriz cruda a cada `<Path>` -- Konva no acepta un
 * atributo `transform` de SVG, pero sí una pila de transforms nativos
 * equivalente.
 */
export function decomposeMatrix(matrix: AffineMatrix): KonvaTransformProps {
  const { a, b, c, d, e, f } = matrix;

  const scaleX = Math.hypot(a, b);
  const rotationRad = Math.atan2(b, a);

  const row0x = scaleX === 0 ? 0 : a / scaleX;
  const row0y = scaleX === 0 ? 0 : b / scaleX;
  const skewDot = row0x * c + row0y * d;
  const row1x = c - skewDot * row0x;
  const row1y = d - skewDot * row0y;
  const scaleY = Math.hypot(row1x, row1y);

  const skewRad = scaleY === 0 ? 0 : Math.atan2(skewDot, scaleY);

  return {
    x: e,
    y: f,
    rotation: (rotationRad * 180) / Math.PI,
    scaleX,
    scaleY,
    skewX: (skewRad * 180) / Math.PI,
  };
}

export interface ParsedLayerPath {
  /** `d` del `<path>` original -- SIN modificar, sigue siendo la fuente de verdad geométrica (ver ADR M2.1-S05, "el dominio nunca deja de serlo"). */
  d: string;
  fill: string;
  transform: KonvaTransformProps;
}

export interface ParsedLayerSvg {
  width: number;
  height: number;
  paths: ParsedLayerPath[];
}

/**
 * Parsea el texto completo de un SVG de capa (ver docstring de arriba) a la
 * lista de `<path>` con su transform ya resuelto (propio + el de cualquier
 * `<g>` ancestro, compuesto en orden SVG estándar) y su color de relleno
 * (propio si lo tiene, si no el `fallbackColorHex` de la capa del dominio --
 * nunca inventa un color que no exista en el SVG ni en `VectorLayer`).
 * Nunca lanza: un SVG que no parsea devuelve `{ width: 0, height: 0, paths: [] }`
 * (fail-safe, igual criterio que el resto de `lib/`).
 */
export function parseLayerSvg(svgText: string, fallbackColorHex: string): ParsedLayerSvg {
  let doc: Document;
  try {
    doc = new DOMParser().parseFromString(svgText, "image/svg+xml");
  } catch {
    return { width: 0, height: 0, paths: [] };
  }

  if (doc.getElementsByTagName("parsererror").length > 0) {
    return { width: 0, height: 0, paths: [] };
  }

  const root = doc.documentElement;
  const width = Number.parseFloat(root.getAttribute("width") ?? "0") || 0;
  const height = Number.parseFloat(root.getAttribute("height") ?? "0") || 0;

  const pathElements = Array.from(doc.getElementsByTagName("path"));
  const paths: ParsedLayerPath[] = pathElements.map((pathEl) => {
    const d = pathEl.getAttribute("d") ?? "";
    const fill = pathEl.getAttribute("fill") || ancestorFill(pathEl) || fallbackColorHex;

    // Compone el transform desde la raíz hacia el path (el ancestro más
    // externo primero), mismo orden que usaría el navegador al renderizar
    // el SVG de verdad.
    const chain: Element[] = [];
    let node: Element | null = pathEl;
    while (node && node !== root) {
      chain.unshift(node);
      node = node.parentElement;
    }

    let combined = IDENTITY_MATRIX;
    for (const el of chain) {
      const local = parseSvgTransformAttribute(el.getAttribute("transform"));
      combined = multiplyMatrices(combined, local);
    }

    return { d, fill, transform: decomposeMatrix(combined) };
  });

  return { width, height, paths };
}

function ancestorFill(element: Element): string | null {
  let node: Element | null = element.parentElement;
  while (node) {
    const fill = node.getAttribute("fill");
    if (fill) return fill;
    node = node.parentElement;
  }
  return null;
}
