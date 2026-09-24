import { THRESHOLD_VALUE_RANGE } from "../../types/threshold";
import type { ThresholdParametersPayload } from "../../types/threshold";

interface ThresholdControlsProps {
  params: ThresholdParametersPayload;
  isLoading: boolean;
  isAtDefaults: boolean;
  onValueChange: (value: number) => void;
  onInvertChange: (value: boolean) => void;
  onReset: () => void;
}

/**
 * Controles de la etapa de threshold B/N ("Usuario podrá: ajustar threshold
 * ...; invertir blanco/negro; ver preview inmediato y resetear" -- spec.md
 * M1-S04). Cada cambio actualiza el estado de React (useThreshold se encarga
 * del debounce); acá no se toca ningún píxel. El modo adaptativo se decidió
 * no incluir en este sprint (ver reporte del sprint).
 */
export function ThresholdControls({
  params,
  isLoading,
  isAtDefaults,
  onValueChange,
  onInvertChange,
  onReset,
}: ThresholdControlsProps) {
  return (
    <div className="threshold-controls">
      <div className="threshold-controls__row">
        <div className="threshold-controls__label-row">
          <label htmlFor="threshold-value">Umbral</label>
          <span className="threshold-controls__value">{params.value}</span>
        </div>
        <input
          id="threshold-value"
          type="range"
          min={THRESHOLD_VALUE_RANGE.min}
          max={THRESHOLD_VALUE_RANGE.max}
          step={THRESHOLD_VALUE_RANGE.step}
          value={params.value}
          onChange={(event) => onValueChange(Number(event.target.value))}
        />
      </div>

      <div className="threshold-controls__row threshold-controls__row--checkbox">
        <label className="threshold-controls__checkbox-label">
          <input
            type="checkbox"
            checked={params.invert}
            onChange={(event) => onInvertChange(event.target.checked)}
          />
          Invertir blanco/negro
        </label>
      </div>

      <div className="threshold-controls__footer">
        <span className="threshold-controls__status" role="status">
          {isLoading ? "Generando máscara…" : ""}
        </span>
        <button type="button" className="upload-actions__button" onClick={onReset} disabled={isAtDefaults}>
          Restablecer valores
        </button>
      </div>
    </div>
  );
}
