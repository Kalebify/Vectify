/**
 * Los fixtures de este spike solo usan `translate(a,b)` y, opcionalmente,
 * `scale(s)` encadenado (ver multicolor-input.svg) -- alcanza con soportar
 * esas dos formas para poder calcular, fuera de cualquier motor gráfico,
 * la posición ABSOLUTA de un anchor point (usado por la demo de edición de
 * segmentos/handles en los 3 candidatos, punto 8 del spike, ver
 * shared/parsePathPoints.ts). No es un parser general de `transform`.
 */
export interface SimpleTransform {
  tx: number;
  ty: number;
  scale: number;
}

export function parseSimpleTransform(transform: string | null): SimpleTransform {
  if (!transform) return { tx: 0, ty: 0, scale: 1 };
  let tx = 0;
  let ty = 0;
  let scale = 1;
  const translateMatch = transform.match(/translate\(\s*(-?[\d.]+)[,\s]+(-?[\d.]+)\s*\)/);
  if (translateMatch) {
    tx = Number(translateMatch[1]);
    ty = Number(translateMatch[2]);
  }
  const scaleMatch = transform.match(/scale\(\s*(-?[\d.]+)\s*\)/);
  if (scaleMatch) {
    scale = Number(scaleMatch[1]);
  }
  return { tx, ty, scale };
}

export function composeAbsolutePoint(
  point: { x: number; y: number },
  pathTransform: string | null,
  groupTransform: string | null
): { x: number; y: number } {
  const pt = parseSimpleTransform(pathTransform);
  const gt = parseSimpleTransform(groupTransform);
  const localX = pt.tx + point.x;
  const localY = pt.ty + point.y;
  return {
    x: gt.tx + gt.scale * localX,
    y: gt.ty + gt.scale * localY,
  };
}
