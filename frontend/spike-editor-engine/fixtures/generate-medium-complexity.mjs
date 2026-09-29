// generate-medium-complexity.mjs
//
// Genera fixtures/medium-complexity.svg: el SVG sintético usado SOLO para el
// benchmark de rendimiento (punto 10 del spike obligatorio, M2.1-S05).
//
// Por qué sintético y no un fixture real del pipeline: los SVG reales
// disponibles en el repo (ver multicolor-input.svg) son o bien triviales
// (4 rectángulos, fixture de paleta de M2.1-S01) o bien un único path con
// ruido de trazado (la "mancha", ~500 subpaths pero en 1 solo elemento
// <path>). Ninguno es representativo de un documento multi-capa con muchas
// capas de color, que es el caso de uso real del futuro Editor General
// (una ilustración vectorizada de complejidad media con varias decenas de
// grupos de color). Se genera entonces un documento paramétrico,
// determinista (seed fija) y documentado, en vez de "inventar a mano" un
// SVG desconectado — la geometría se genera por fórmula, no a ojo.
//
// Definición de "complejidad media" adoptada para este spike (documentada
// también en el ADR): 220 <path> (simulando 220 capas/grupos de color,
// diez veces más que el caso real más grande visto en QA del pipeline
// hasta M2.1-S04, que tenía como máximo ~20-30 grupos de color) x ~22
// nodos por path en promedio (mezcla de segmentos L y C, para ejercitar
// tanto polígonos como curvas Bézier) = ~4,840 nodos totales, repartidos
// en un lienzo de 2000x2000. Esto es deliberadamente más exigente que un
// documento típico de producción, para que el benchmark tenga margen antes
// de degradarse.
//
// Uso: node fixtures/generate-medium-complexity.mjs > fixtures/medium-complexity.svg
// (ya ejecutado una vez; el archivo resultante queda commiteado como
// fixture reproducible, no hace falta volver a correrlo salvo para
// regenerar con otros parámetros.)

const PATH_COUNT = 220;
const NODES_PER_PATH_MIN = 16;
const NODES_PER_PATH_MAX = 28;
const CANVAS = 2000;

// PRNG determinista (mulberry32) para que el fixture sea reproducible byte
// a byte entre corridas.
function mulberry32(seed) {
  let a = seed;
  return function () {
    a |= 0;
    a = (a + 0x6d2b79f5) | 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}
const rand = mulberry32(1337);

function hslToHex(h, s, l) {
  s /= 100;
  l /= 100;
  const k = (n) => (n + h / 30) % 12;
  const a = s * Math.min(l, 1 - l);
  const f = (n) => l - a * Math.max(-1, Math.min(k(n) - 3, Math.min(9 - k(n), 1)));
  const toHex = (x) => Math.round(255 * x).toString(16).padStart(2, "0");
  return `#${toHex(f(0))}${toHex(f(8))}${toHex(f(4))}`;
}

function generatePath(cx, cy, radius, nodeCount) {
  const points = [];
  for (let i = 0; i < nodeCount; i++) {
    const angle = (i / nodeCount) * Math.PI * 2;
    const r = radius * (0.6 + rand() * 0.4);
    points.push([cx + Math.cos(angle) * r, cy + Math.sin(angle) * r]);
  }
  let d = `M${points[0][0].toFixed(1)},${points[0][1].toFixed(1)} `;
  for (let i = 1; i < points.length; i++) {
    const [x, y] = points[i];
    if (i % 2 === 0) {
      // Segmento Bezier cubico (C) usando el punto previo como referencia
      // de control aproximada, para mezclar curvas con líneas rectas.
      const [px, py] = points[i - 1];
      const c1x = px + (x - px) * 0.33;
      const c1y = py + (y - py) * 0.1;
      const c2x = px + (x - px) * 0.66;
      const c2y = y + (py - y) * 0.1;
      d += `C${c1x.toFixed(1)},${c1y.toFixed(1)} ${c2x.toFixed(1)},${c2y.toFixed(1)} ${x.toFixed(1)},${y.toFixed(1)} `;
    } else {
      d += `L${x.toFixed(1)},${y.toFixed(1)} `;
    }
  }
  d += "Z";
  return d;
}

const groups = [];
const cols = 20;
const rows = Math.ceil(PATH_COUNT / cols);
const cellW = CANVAS / cols;
const cellH = CANVAS / rows;

for (let i = 0; i < PATH_COUNT; i++) {
  const col = i % cols;
  const row = Math.floor(i / cols);
  const cx = cellW * col + cellW / 2;
  const cy = cellH * row + cellH / 2;
  const radius = Math.min(cellW, cellH) * (0.3 + rand() * 0.15);
  const nodeCount =
    NODES_PER_PATH_MIN +
    Math.floor(rand() * (NODES_PER_PATH_MAX - NODES_PER_PATH_MIN));
  const hue = Math.floor(rand() * 360);
  const fill = hslToHex(hue, 55 + rand() * 30, 40 + rand() * 20);
  const d = generatePath(cx, cy, radius, nodeCount);
  const groupId = `bench-${String(i).padStart(4, "0")}`;
  groups.push(
    `<g id="layer-${groupId}" data-group-id="${groupId}" data-name="Bench ${i}" data-color-hex="${fill}" data-vector-id="${groupId}" data-z-index="${i}" data-visible="true" fill="${fill}"><path d="${d}" /></g>`
  );
}

const totalNodesApprox = PATH_COUNT * ((NODES_PER_PATH_MIN + NODES_PER_PATH_MAX) / 2);

const svg = `<svg xmlns="http://www.w3.org/2000/svg" version="1.1" viewBox="0 0 ${CANVAS} ${CANVAS}" width="${CANVAS}" height="${CANVAS}">
<!--
  medium-complexity.svg — GENERADO por generate-medium-complexity.mjs (seed=1337).
  ${PATH_COUNT} paths, ~${NODES_PER_PATH_MIN}-${NODES_PER_PATH_MAX} nodos c/u,
  ~${totalNodesApprox} nodos totales. Ver ese script y el ADR (IMPL.md) para
  la definición de "complejidad media" adoptada y la justificación de por
  qué es sintético.
-->
${groups.join("\n")}
</svg>
`;

process.stdout.write(svg);
