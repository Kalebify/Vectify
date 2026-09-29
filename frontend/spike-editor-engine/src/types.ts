/**
 * Tipos compartidos del spike M2.1-S05.
 *
 * SpikeLayer espeja (deliberadamente, con nombres análogos) los campos del
 * modelo de dominio REAL VectorLayer (backend/Vectorify.Api/VectorLayers/VectorLayer.cs):
 * GroupId, Name, ColorHex, VectorId — más `paths`, la geometría cruda
 * (un `d` de SVG por <path> hijo del <g> de capa) y `zIndex`/`visible`,
 * que en producción vive en el estado de React (ver M2.1-S04), NO en el
 * motor gráfico. Cada candidato del spike recibe la MISMA lista de
 * SpikeLayer (parseada una sola vez desde el SVG fixture) y decide cómo
 * la traduce a sus propios objetos internos (Path/Group de Paper.js,
 * fabric.Path/Group, Konva.Path/Group...). Este archivo es el único
 * "contrato" compartido entre los 3 candidatos — a propósito, para que la
 * comparación sea justa (misma entrada, mismo shape de datos).
 */
export interface SpikeLayer {
  /** id DOM del <g> de origen (ej. "layer-donut") */
  id: string;
  /** ~ VectorLayer.GroupId */
  groupId: string;
  /** ~ VectorLayer.Name */
  name: string;
  /** ~ VectorLayer.ColorHex */
  colorHex: string;
  /** ~ VectorLayer.VectorId */
  vectorId: string;
  /** orden de apilado, ~ orden de capas en M2.1-S04 */
  zIndex: number;
  /** visibilidad inicial declarada en el SVG (data-visible) */
  visible: boolean;
  /** transform del <g> contenedor, aplicado tal cual (string SVG transform) */
  groupTransform: string | null;
  /** un `d` de SVG por cada <path> hijo, con su propio transform si lo tenía */
  paths: Array<{ d: string; transform: string | null }>;
}

export interface SpikeDocument {
  width: number;
  height: number;
  viewBox: string;
  layers: SpikeLayer[];
}

/** Resultado de una medición de rendimiento, común a los 3 candidatos. */
export interface PerfResult {
  candidate: string;
  fixture: string;
  pathCount: number;
  approxNodeCount: number;
  importMs: number;
  avgFrameMs: number;
  fps: number;
  sampleFrames: number;
}
