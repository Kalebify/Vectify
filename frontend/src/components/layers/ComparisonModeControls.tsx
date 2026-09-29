import type { ComparisonMode } from "../../hooks/useComparisonMode";

interface ComparisonModeControlsProps {
  mode: ComparisonMode;
  onChangeMode: (mode: ComparisonMode) => void;
  /** false si no hay imagen original disponible para comparar (deshabilita esa opción sin ocultarla). */
  hasOriginal: boolean;
  /** false si no hay ninguna capa seleccionada todavía (deshabilita "Layer aislado" sin ocultarla). */
  hasSelection: boolean;
}

/**
 * Modo de comparación de spec.md M2.1-S04 ("alternar Original/Compuesto/
 * Layer aislado sin cambiar datos"): radio group semántico (mismo criterio
 * que ExplodedViewControls, M2-S04/MVP2) -- mutuamente excluyente y anunciado
 * como tal. Puramente visual: ver useComparisonMode.
 */
export function ComparisonModeControls({ mode, onChangeMode, hasOriginal, hasSelection }: ComparisonModeControlsProps) {
  return (
    <fieldset className="comparison-mode-controls">
      <legend className="comparison-mode-controls__legend">Comparar</legend>
      <div className="comparison-mode-controls__options">
        <label className="comparison-mode-controls__option">
          <input
            type="radio"
            name="comparison-mode"
            value="original"
            checked={mode === "original"}
            disabled={!hasOriginal}
            onChange={() => onChangeMode("original")}
          />
          Original
        </label>
        <label className="comparison-mode-controls__option">
          <input
            type="radio"
            name="comparison-mode"
            value="composite"
            checked={mode === "composite"}
            onChange={() => onChangeMode("composite")}
          />
          Compuesto
        </label>
        <label className="comparison-mode-controls__option">
          <input
            type="radio"
            name="comparison-mode"
            value="isolated"
            checked={mode === "isolated"}
            disabled={!hasSelection}
            onChange={() => onChangeMode("isolated")}
          />
          Layer aislado
        </label>
      </div>
    </fieldset>
  );
}
