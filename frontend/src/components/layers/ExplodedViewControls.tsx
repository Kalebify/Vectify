import type { ExplodedViewMode } from "../../hooks/useExplodedView";

interface ExplodedViewControlsProps {
  viewMode: ExplodedViewMode;
  onChangeViewMode: (mode: ExplodedViewMode) => void;
  separationPercent: number;
  onChangeSeparationPercent: (value: number) => void;
}

/**
 * Toggle explícito "ensamblada"/"explotada" + control de separación visual
 * de spec.md M2-S04. Radio group semántico (no un `<div>` con onClick) para
 * que "ensamblada ACTIVA vs. explotada ACTIVA" se anuncie como una elección
 * mutuamente excluyente -- mismo criterio que
 * `.check-panel__source-options` (M1-S08) / `.dimension-panel__source-options`
 * (M1-S09).
 *
 * El control de separación es `type="number"` (no `type="range"`): el
 * criterio de aceptación pide explícitamente "sin límite superior estricto",
 * y un `<input type="range">` sólo puede expresar eso fijando un `max`
 * arbitrariamente alto que de todos modos sigue siendo un tope duro que el
 * usuario no puede superar desde la UI -- `number` sin atributo `max` es la
 * única forma de que pueda subir la separación tanto como quiera (solo se
 * valida "no negativo" en `useExplodedView.setSeparationPercent`).
 */
export function ExplodedViewControls({
  viewMode,
  onChangeViewMode,
  separationPercent,
  onChangeSeparationPercent,
}: ExplodedViewControlsProps) {
  return (
    <div className="exploded-view-controls">
      <fieldset className="exploded-view-controls__mode">
        <legend className="exploded-view-controls__mode-legend">Vista</legend>
        <div className="exploded-view-controls__mode-options">
          <label className="exploded-view-controls__mode-option">
            <input
              type="radio"
              name="exploded-view-mode"
              value="assembled"
              checked={viewMode === "assembled"}
              onChange={() => onChangeViewMode("assembled")}
            />
            Ensamblada
          </label>
          <label className="exploded-view-controls__mode-option">
            <input
              type="radio"
              name="exploded-view-mode"
              value="exploded"
              checked={viewMode === "exploded"}
              onChange={() => onChangeViewMode("exploded")}
            />
            Explotada
          </label>
        </div>
      </fieldset>

      {viewMode === "exploded" && (
        <div className="exploded-view-controls__separation">
          <label htmlFor="exploded-view-separation">Separación entre capas</label>
          <input
            id="exploded-view-separation"
            type="number"
            min={0}
            step={5}
            value={separationPercent}
            onChange={(event) => onChangeSeparationPercent(event.target.valueAsNumber)}
          />
          <span className="exploded-view-controls__separation-unit" aria-hidden="true">
            %
          </span>
        </div>
      )}
    </div>
  );
}
