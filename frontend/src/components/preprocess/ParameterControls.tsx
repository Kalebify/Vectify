import { BRIGHTNESS_RANGE, CONTRAST_RANGE, DENOISE_RANGE } from "../../types/preprocess";
import type { PreprocessParametersPayload } from "../../types/preprocess";

interface ParameterControlsProps {
  params: PreprocessParametersPayload;
  isLoading: boolean;
  isAtDefaults: boolean;
  onGrayscaleChange: (value: boolean) => void;
  onContrastChange: (value: number) => void;
  onBrightnessChange: (value: number) => void;
  onDenoiseChange: (value: number) => void;
  onReset: () => void;
}

/**
 * Panel de sliders del pipeline de preprocesamiento ("Usuario podrá: ajustar
 * escala de grises, contraste, brillo/suavizado y reducción de ruido;
 * restablecer valores" — spec.md M1-S03). Cada cambio actualiza el estado de
 * React (usePreprocess se encarga del debounce); acá no se toca ningún píxel.
 */
export function ParameterControls({
  params,
  isLoading,
  isAtDefaults,
  onGrayscaleChange,
  onContrastChange,
  onBrightnessChange,
  onDenoiseChange,
  onReset,
}: ParameterControlsProps) {
  return (
    <div className="preprocess-controls">
      <div className="preprocess-controls__row preprocess-controls__row--checkbox">
        <label className="preprocess-controls__checkbox-label">
          <input
            type="checkbox"
            checked={params.grayscale}
            onChange={(event) => onGrayscaleChange(event.target.checked)}
          />
          Escala de grises
        </label>
      </div>

      <div className="preprocess-controls__row">
        <div className="preprocess-controls__label-row">
          <label htmlFor="preprocess-contrast">Contraste</label>
          <span className="preprocess-controls__value">{params.contrast.toFixed(1)}</span>
        </div>
        <input
          id="preprocess-contrast"
          type="range"
          min={CONTRAST_RANGE.min}
          max={CONTRAST_RANGE.max}
          step={CONTRAST_RANGE.step}
          value={params.contrast}
          onChange={(event) => onContrastChange(Number(event.target.value))}
        />
      </div>

      <div className="preprocess-controls__row">
        <div className="preprocess-controls__label-row">
          <label htmlFor="preprocess-brightness">Brillo</label>
          <span className="preprocess-controls__value">{params.brightness}</span>
        </div>
        <input
          id="preprocess-brightness"
          type="range"
          min={BRIGHTNESS_RANGE.min}
          max={BRIGHTNESS_RANGE.max}
          step={BRIGHTNESS_RANGE.step}
          value={params.brightness}
          onChange={(event) => onBrightnessChange(Number(event.target.value))}
        />
      </div>

      <div className="preprocess-controls__row">
        <div className="preprocess-controls__label-row">
          <label htmlFor="preprocess-denoise">Suavizado / reducción de ruido</label>
          <span className="preprocess-controls__value">{params.denoise}</span>
        </div>
        <input
          id="preprocess-denoise"
          type="range"
          min={DENOISE_RANGE.min}
          max={DENOISE_RANGE.max}
          step={DENOISE_RANGE.step}
          value={params.denoise}
          onChange={(event) => onDenoiseChange(Number(event.target.value))}
        />
      </div>

      <div className="preprocess-controls__footer">
        <span className="preprocess-controls__status" role="status">
          {isLoading ? "Generando preview…" : ""}
        </span>
        <button type="button" className="upload-actions__button" onClick={onReset} disabled={isAtDefaults}>
          Restablecer valores
        </button>
      </div>
    </div>
  );
}
