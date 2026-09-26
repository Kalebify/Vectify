import { useCallback, useState } from "react";

/** "assembled" = capas superpuestas en su posición real (igual que M2-S02). "exploded" = capas desplazadas para inspección (M2-S04). */
export type ExplodedViewMode = "assembled" | "exploded";

const DEFAULT_SEPARATION_PERCENT = 20;

export interface UseExplodedViewState {
  viewMode: ExplodedViewMode;
  separationPercent: number;
  setViewMode: (mode: ExplodedViewMode) => void;
  toggleViewMode: () => void;
  /** Ignora valores no finitos o negativos -- ver docstring de la clase. */
  setSeparationPercent: (value: number) => void;
}

/**
 * Estado puramente visual de la vista explotada (spec.md M2-S04, "Usuario
 * podrá: alternar vista ensamblada/explotada ... controlar separación
 * visual"). Nunca toca layerSet/visibility (M2-S02) ni componentsByGroup
 * (M2-S03) -- solo decide CÓMO se dibuja lo que esos hooks ya calcularon,
 * mismo criterio que useCanvasTransform (M1-S06): "la visualización no
 * modifica el recurso subyacente". Vive en LayersPanel (no remonta con
 * `key` como useVectorLayers/useLayerComponents) para que cambiar de vista
 * nunca reinicie la selección ni la separación elegida.
 *
 * `separationPercent` no tiene límite superior estricto (ver spec.md,
 * "Control de separación visual: ... sin límite superior estricto"): solo
 * se descarta un valor no finito o negativo, para no romper el layout con
 * NaN o invertir el sentido del desplazamiento.
 */
export function useExplodedView(): UseExplodedViewState {
  const [viewMode, setViewMode] = useState<ExplodedViewMode>("assembled");
  const [separationPercent, setSeparationPercentState] = useState<number>(DEFAULT_SEPARATION_PERCENT);

  const toggleViewMode = useCallback(() => {
    setViewMode((current) => (current === "assembled" ? "exploded" : "assembled"));
  }, []);

  const setSeparationPercent = useCallback((value: number) => {
    if (!Number.isFinite(value) || value < 0) {
      return;
    }
    setSeparationPercentState(value);
  }, []);

  return { viewMode, separationPercent, setViewMode, toggleViewMode, setSeparationPercent };
}
