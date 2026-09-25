interface DimensionControlsProps {
  widthInput: string;
  heightInput: string;
  lockAspectRatio: boolean;
  disabled: boolean;
  onWidthChange: (value: string) => void;
  onHeightChange: (value: string) => void;
  onLockChange: (locked: boolean) => void;
}

/**
 * Inputs de ancho/alto en mm + toggle de proporción bloqueada/desbloqueada
 * (spec.md M1-S09: "Definir ancho o alto en mm, bloquear/desbloquear
 * proporción"). Con la proporción bloqueada (default), ancho y alto son
 * mutuamente excluyentes -- completar uno limpia el otro (ver
 * useDimensions.setWidthInput/setHeightInput) -- así que ambos inputs
 * conviven siempre habilitados, sin deshabilitar ninguno explícitamente: el
 * propio flujo de datos ya impone la exclusividad.
 */
export function DimensionControls({
  widthInput,
  heightInput,
  lockAspectRatio,
  disabled,
  onWidthChange,
  onHeightChange,
  onLockChange,
}: DimensionControlsProps) {
  return (
    <fieldset className="dimension-controls" disabled={disabled}>
      <legend className="dimension-controls__legend">Dimensiones físicas</legend>

      <div className="dimension-controls__row">
        <div className="dimension-controls__field">
          <label htmlFor="dimension-width">Ancho (mm)</label>
          <input
            id="dimension-width"
            type="number"
            inputMode="decimal"
            step="any"
            value={widthInput}
            onChange={(event) => onWidthChange(event.target.value)}
            placeholder="Ancho en mm"
          />
        </div>

        <div className="dimension-controls__field">
          <label htmlFor="dimension-height">Alto (mm)</label>
          <input
            id="dimension-height"
            type="number"
            inputMode="decimal"
            step="any"
            value={heightInput}
            onChange={(event) => onHeightChange(event.target.value)}
            placeholder="Alto en mm"
          />
        </div>
      </div>

      <label className="dimension-controls__lock-label">
        <input
          type="checkbox"
          checked={lockAspectRatio}
          onChange={(event) => onLockChange(event.target.checked)}
        />
        Proporción bloqueada
      </label>
      <p className="dimension-controls__lock-hint">
        {lockAspectRatio
          ? "Completá solo ancho o alto: el otro se calcula automáticamente para mantener la proporción original."
          : "Ancho y alto son independientes: si no mantienen la proporción original, el diseño se deforma (estiramiento no uniforme)."}
      </p>
    </fieldset>
  );
}
