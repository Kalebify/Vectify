/**
 * Parser MINIMO de comandos M/L (absolutos) de un `d` de SVG, a propósito
 * deliberadamente simple: solo se usa para el punto 8 del spike
 * ("viabilidad de selección de segmentos/handles para Bézier futuro"), NO
 * es un parser de producción. Los fixtures de este spike solo usan M/L/Z
 * (más C en medium-complexity.svg, que esta demo no usa). Sirve para
 * extraer los anchor points de UN path de ejemplo y dejarlos
 * seleccionables/arrastrables en los 3 candidatos con la MISMA lista de
 * puntos de partida, de forma comparable.
 */
export interface AnchorPoint {
  index: number;
  x: number;
  y: number;
}

export function parseAnchorPoints(d: string): AnchorPoint[] {
  const points: AnchorPoint[] = [];
  const commandRegex = /([ML])\s*(-?[\d.]+)[,\s]+(-?[\d.]+)/g;
  let match: RegExpExecArray | null;
  let index = 0;
  while ((match = commandRegex.exec(d)) !== null) {
    const [, , xStr, yStr] = match;
    points.push({ index: index++, x: Number(xStr), y: Number(yStr) });
  }
  return points;
}

/** Reconstruye un `d` M/L/Z a partir de anchor points editados. */
export function rebuildPathFromPoints(points: AnchorPoint[]): string {
  if (points.length === 0) return "";
  const [first, ...rest] = points;
  const segments = rest.map((p) => `L${p.x.toFixed(1)},${p.y.toFixed(1)}`);
  return `M${first.x.toFixed(1)},${first.y.toFixed(1)} ${segments.join(" ")} Z`;
}
