import '@testing-library/jest-dom/vitest';
import { cleanup } from '@testing-library/react';
import { afterEach, vi } from 'vitest';

// jsdom no implementa ResizeObserver (usado por VectorCanvas, M1-S06, para
// medir el contenedor y calcular fit-to-screen). Stub mínimo: no dispara
// callbacks automáticamente (los tests que necesiten simular una medición
// llaman a la callback registrada explícitamente), pero evita un
// ReferenceError al montar el componente.
if (typeof globalThis.ResizeObserver === 'undefined') {
  class ResizeObserverStub {
    observe() {}
    unobserve() {}
    disconnect() {}
  }
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  (globalThis as any).ResizeObserver = ResizeObserverStub;
}

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  vi.useRealTimers();
});
