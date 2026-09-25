import type { SimplifyPreset } from "../../types/simplify";

interface SimplifyControlsProps {
  preset: SimplifyPreset;
  disabled: boolean;
  onPresetChange: (preset: SimplifyPreset) => void;
}

const PRESET_OPTIONS: Array<{ value: SimplifyPreset; label: string; hint: string }> = [
  { value: "low", label: "Bajo", hint: "Cambios mínimos: conserva casi todo el detalle." },
  { value: "medium", label: "Medio", hint: "Reducción moderada: punto de partida recomendado." },
  { value: "high", label: "Alto", hint: "Reducción agresiva: prioriza menos nodos sobre el detalle fino." },
];

/**
 * Selector de tolerancia Bajo/Medio/Alto (spec.md M1-S07: "Elegir
 * tolerancia/preset Bajo-Medio-Alto"). Los tres presets se resuelven a un
 * epsilon de Douglas-Peucker del lado de ASP.NET Core (ver
 * Vectify.Api.Options.SimplificationOptions) -- React solo conoce el nombre
 * del preset, nunca el valor numérico. Radiogroup nativo (no botones
 * separados con estado propio) para que la selección tenga semántica y foco
 * de teclado correctos sin ARIA adicional.
 */
export function SimplifyControls({ preset, disabled, onPresetChange }: SimplifyControlsProps) {
  return (
    <fieldset className="simplify-controls" disabled={disabled}>
      <legend className="simplify-controls__legend">Tolerancia de simplificación</legend>
      <div className="simplify-controls__options" role="radiogroup" aria-label="Tolerancia de simplificación">
        {PRESET_OPTIONS.map((option) => (
          <label key={option.value} className="simplify-controls__option">
            <input
              type="radio"
              name="simplify-preset"
              value={option.value}
              checked={preset === option.value}
              onChange={() => onPresetChange(option.value)}
              // Nombre accesible explícito (solo "Bajo"/"Medio"/"Alto"): sin esto,
              // el <label> que envuelve el input incluye también el texto del
              // hint en el nombre accesible computado, lo que hace frágil
              // cualquier selector por rol+nombre ("Alto" dejaría de matchear
              // si el texto del hint cambia).
              aria-label={option.label}
              aria-describedby={`simplify-preset-hint-${option.value}`}
            />
            <span className="simplify-controls__option-label" aria-hidden="true">
              {option.label}
            </span>
            <span id={`simplify-preset-hint-${option.value}`} className="simplify-controls__option-hint">
              {option.hint}
            </span>
          </label>
        ))}
      </div>
    </fieldset>
  );
}
