import { useEffect } from "react";
import { getColorPalettePreviewUrl } from "../../api/colorPaletteApi";
import { useColorPalette } from "../../hooks/useColorPalette";
import type { ColorPaletteResponse } from "../../types/colorPalette";
import { ColorSwatchList } from "./ColorSwatchList";

interface ColorPalettePanelProps {
  projectId: string;
  imageId: string;
  fileName: string;
  originalUrl: string;
  originalWidth: number;
  originalHeight: number;
  /**
   * Notifica a quien orquesta (App) cada vez que la última versión vigente
   * de la paleta está confirmada -- M2-S02 (Layers) consume la paleta
   * CONFIRMADA, así que el panel de capas solo puede activarse a partir de
   * este evento. Se llama de nuevo (con la misma paleta) si el componente
   * vuelve a renderizar en estado confirmado -- idempotente del lado de
   * quien escuche, no solo la primera vez.
   */
  onConfirmed?: (palette: ColorPaletteResponse) => void;
}

const STATUS_LABEL: Record<string, string> = {
  idle: "",
  detecting: "Detectando paleta de colores…",
  ready: "Paleta detectada. Fusioná, renombrá o confirmá los grupos.",
  mutating: "Aplicando cambios…",
  confirmed: "Paleta confirmada: lista como entrada para generar las capas por color.",
  error: "",
};

/**
 * Orquesta el flujo de "Usuario podrá" de spec.md M2-S01: subir/usar una
 * imagen en color, ver la paleta detectada y el número de colores, fusionar
 * colores parecidos, renombrar grupos y confirmar la paleta. Arranca el
 * flujo multicapa operando directamente sobre la imagen original YA subida
 * (M1-S02) -- no depende de ninguna otra etapa del pipeline de MVP1.
 */
export function ColorPalettePanel({
  projectId,
  imageId,
  fileName,
  originalUrl,
  originalWidth,
  originalHeight,
  onConfirmed,
}: ColorPalettePanelProps) {
  const {
    status,
    palette,
    tolerance,
    maxColors,
    selectedGroupIds,
    errorMessage,
    setTolerance,
    setMaxColors,
    detect,
    toggleGroupSelection,
    clearSelection,
    mergeSelected,
    unmerge,
    rename,
    confirm,
  } = useColorPalette(projectId, imageId);

  useEffect(() => {
    if (palette?.isConfirmed) {
      onConfirmed?.(palette);
    }
    // Notificar de nuevo con la MISMA `palette` si `onConfirmed` cambia de
    // identidad (ej. el padre re-renderiza con una arrow function inline)
    // es inofensivo: App.tsx solo hace `setConfirmedPalette(palette)`, y
    // asignar el mismo objeto de referencia no dispara un re-render extra.
  }, [palette, onConfirmed]);

  const isBusy = status === "detecting" || status === "mutating";
  const isConfirmed = status === "confirmed";
  const canEdit = palette !== null && !isConfirmed && !isBusy;
  const canMerge = canEdit && selectedGroupIds.length >= 2;
  const canConfirm = canEdit && palette !== null && palette.groups.length > 0;

  const previewUrl = palette ? `${getColorPalettePreviewUrl(projectId, imageId, palette.paletteId)}?v=${palette.version}` : null;

  return (
    <div className="color-palette-panel">
      <div className="color-palette-controls">
        <div className="color-palette-controls__row">
          <div className="color-palette-controls__label-row">
            <label htmlFor="color-palette-tolerance">Tolerancia</label>
            <span className="color-palette-controls__value">{tolerance}</span>
          </div>
          <input
            id="color-palette-tolerance"
            type="range"
            min={0}
            max={100}
            step={1}
            value={tolerance}
            disabled={isBusy}
            onChange={(event) => setTolerance(Number(event.target.value))}
          />
        </div>

        <div className="color-palette-controls__row">
          <label htmlFor="color-palette-max-colors">Número máximo de colores (opcional)</label>
          <input
            id="color-palette-max-colors"
            type="number"
            min={1}
            max={64}
            value={maxColors ?? ""}
            disabled={isBusy}
            placeholder="Sin límite"
            onChange={(event) => {
              const raw = event.target.value;
              setMaxColors(raw === "" ? null : Number(raw));
            }}
          />
        </div>

        <div className="color-palette-controls__footer">
          <button
            type="button"
            className="upload-actions__button upload-actions__button--primary"
            onClick={detect}
            disabled={isBusy || isConfirmed}
          >
            {palette ? "Detectar de nuevo" : "Detectar paleta"}
          </button>
          <span className="color-palette-controls__status" role="status">
            {STATUS_LABEL[status]}
          </span>
        </div>
      </div>

      {status === "error" && errorMessage && (
        <p className="upload-panel__error" role="alert">
          {errorMessage}
        </p>
      )}

      {palette && (
        <>
          <dl className="service-card__details color-palette-panel__summary">
            <div>
              <dt>Colores detectados</dt>
              <dd>{palette.groups.length}</dd>
            </div>
            <div>
              <dt>Transparencia</dt>
              <dd>{palette.transparentPercent.toFixed(1)}%</dd>
            </div>
            <div>
              <dt>Estado</dt>
              <dd>{palette.isConfirmed ? "Confirmada" : "Editable"}</dd>
            </div>
          </dl>

          <div className="color-palette-comparison">
            <figure className="color-palette-comparison__item">
              <img src={originalUrl} alt={`Imagen original de ${fileName}`} width={originalWidth} height={originalHeight} loading="lazy" />
              <figcaption>Original</figcaption>
            </figure>
            <figure className="color-palette-comparison__item">
              {previewUrl && (
                <img
                  src={previewUrl}
                  alt={`Preview cuantizado de ${fileName} con los colores agrupados`}
                  width={palette.sourceWidthPx}
                  height={palette.sourceHeightPx}
                  loading="lazy"
                />
              )}
              <figcaption>Preview cuantizado</figcaption>
            </figure>
          </div>

          <ColorSwatchList
            groups={palette.groups}
            selectedGroupIds={selectedGroupIds}
            disabled={!canEdit}
            onToggleSelection={toggleGroupSelection}
            onRename={rename}
            onUnmerge={unmerge}
          />

          <div className="color-palette-panel__actions">
            <button
              type="button"
              className="upload-actions__button"
              onClick={() => mergeSelected()}
              disabled={!canMerge}
            >
              Fusionar seleccionados ({selectedGroupIds.length})
            </button>
            <button
              type="button"
              className="upload-actions__button"
              onClick={clearSelection}
              disabled={!canEdit || selectedGroupIds.length === 0}
            >
              Limpiar selección
            </button>
            <button
              type="button"
              className="upload-actions__button upload-actions__button--primary"
              onClick={confirm}
              disabled={!canConfirm}
            >
              Confirmar paleta
            </button>
          </div>
        </>
      )}

      {status === "idle" && (
        <p className="color-palette-panel__hint">
          Pulsá &quot;Detectar paleta&quot; para agrupar los colores de la imagen original. Podés ajustar la
          tolerancia y el número máximo de colores antes de detectar, y volver a fusionar/renombrar grupos después.
        </p>
      )}
    </div>
  );
}
