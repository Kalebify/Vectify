import type { PerfResult } from "../types";

/**
 * Harness de medición de rendimiento COMÚN a los 3 candidatos (punto 10 del
 * spike). Mide dos cosas, de la misma forma para los 3:
 *
 * 1. importMs: tiempo real (performance.now) entre "arrancar a construir la
 *    escena a partir de SpikeDocument" y "el motor terminó de crear todos
 *    los nodos/objetos" (medido por cada candidato alrededor de su propio
 *    bucle de creación, ver candidates/*​/​*Spike.tsx).
 * 2. avgFrameMs/fps: se samplean N frames con requestAnimationFrame
 *    mientras se aplica una transformación continua (pan) sobre el
 *    documento ya importado, y se promedia el delta entre frames.
 *
 * Método de medición documentado también en el ADR (IMPL.md): no es un
 * benchmark de laboratorio con warm-up/statistics avanzadas — es una
 * medición real pero simple (mismo método, mismo fixture, misma máquina,
 * para los 3), suficiente para una decisión informada, no para un paper.
 */
export function measureImport<T>(fn: () => T): { result: T; importMs: number } {
  const start = performance.now();
  const result = fn();
  const importMs = performance.now() - start;
  return { result, importMs };
}

export function sampleFrameRate(
  sampleFrames: number,
  onFrame: (elapsedMs: number) => void
): Promise<{ avgFrameMs: number; fps: number }> {
  return new Promise((resolve) => {
    const deltas: number[] = [];
    let last = performance.now();
    let count = 0;

    function tick() {
      const now = performance.now();
      const delta = now - last;
      last = now;
      if (count > 0) {
        // Se descarta el primer frame (incluye costo de arranque del rAF).
        deltas.push(delta);
      }
      onFrame(now);
      count++;
      if (count <= sampleFrames) {
        requestAnimationFrame(tick);
      } else {
        const avgFrameMs = deltas.reduce((a, b) => a + b, 0) / deltas.length;
        resolve({ avgFrameMs, fps: 1000 / avgFrameMs });
      }
    }
    requestAnimationFrame(tick);
  });
}

export function buildPerfResult(partial: Omit<PerfResult, "fps">): PerfResult {
  return { ...partial, fps: 1000 / partial.avgFrameMs };
}
