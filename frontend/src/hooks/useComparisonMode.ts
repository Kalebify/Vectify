import { useCallback, useState } from "react";

/**
 * Modo de comparación de spec.md M2.1-S04 ("Usuario podrá: alternar
 * Original/Compuesto/Layer aislado sin cambiar datos"):
 * - "original": la imagen raster subida tal cual (M1-S02).
 * - "composite": la composición de todas las capas VISIBLES -- el canvas
 *   combinado que ya existe desde M2-S02, sin cambios.
 * - "isolated": únicamente el layer actualmente seleccionado (ver
 *   selectedGroupId compartido con la paleta), calculado en el render (no
 *   escribe en `visibility`) -- así alternar a este modo y volver a
 *   "composite" nunca pierde ni un Isolate ni un ocultamiento manual previo.
 */
export type ComparisonMode = "original" | "composite" | "isolated";

export interface UseComparisonModeState {
  mode: ComparisonMode;
  setMode: (mode: ComparisonMode) => void;
}

/**
 * Puramente visual, como useExplodedView (M2-S04/MVP2): nunca toca
 * layerSet/visibility (M2-S02) ni componentsByGroup (M2-S03), nunca persiste
 * nada. Vive en LayersPanel (no remonta con `key`) para que cambiar de modo
 * nunca reinicie la selección ni la visibilidad vigente.
 */
export function useComparisonMode(): UseComparisonModeState {
  const [mode, setModeState] = useState<ComparisonMode>("composite");

  const setMode = useCallback((next: ComparisonMode) => {
    setModeState(next);
  }, []);

  return { mode, setMode };
}
