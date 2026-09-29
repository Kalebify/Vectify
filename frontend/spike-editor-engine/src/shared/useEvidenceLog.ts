import { useState, useCallback } from "react";

export const SPIKE_POINTS = [
  "1. Importar SVG",
  "2. Layers/IDs sin pérdida",
  "3. Zoom/Pan",
  "4. Single + multi-select",
  "5. Move/scale/rotate",
  "6. Hide/show/isolate layer",
  "7. Hit-test de path",
  "8. Segmentos/handles (Bézier)",
  "9. Export/round-trip",
  "10. Performance (SVG medio)",
] as const;

export type SpikePoint = (typeof SPIKE_POINTS)[number];

/**
 * Estado compartido de "evidencia" que cada candidato va marcando a medida
 * que el usuario ejercita cada uno de los 10 puntos del spike obligatorio
 * en la demo. No es telemetría de producción — es simplemente para que la
 * demo deje constancia visible (en pantalla y en consola) de qué se probó.
 */
export function useEvidenceLog() {
  const [logs, setLogs] = useState<string[]>([]);
  const [done, setDone] = useState<Set<SpikePoint>>(new Set());

  const log = useCallback((point: SpikePoint | null, message: string) => {
    const line = `[${new Date().toLocaleTimeString()}] ${message}`;
    // eslint-disable-next-line no-console
    console.log(line);
    setLogs((prev) => [line, ...prev].slice(0, 60));
    if (point) {
      setDone((prev) => new Set(prev).add(point));
    }
  }, []);

  return { logs, done, log };
}
