import { useEffect, useState } from "react";
import { getDimensionedSvgUrl } from "../../api/dimensionApi";
import { useDimensions, type DimensionStatus } from "../../hooks/useDimensions";
import type { DimensionResponse, DimensionSourceKind } from "../../types/dimension";
import { DimensionControls } from "./DimensionControls";

/** Un SVG ya generado (vectorizado o simplificado) que el usuario puede elegir como fuente -- mismo criterio dual que CheckSourceOption (M1-S08). */
export interface DimensionSourceOption {
  kind: DimensionSourceKind;
  id: string;
  label: string;
  widthPx: number;
  heightPx: number;
}

interface DimensionPanelProps {
  projectId: string;
  imageId: string;
  /** Al menos una fuente (el vector actual); una segunda opcional si ya hay una simplificación aplicada. */
  sources: DimensionSourceOption[];
  /**
   * Notifica al padre cada vez que hay una DimensionVersion nueva APLICADA
   * (persistida) -- mismo criterio que SimplifyPanel.onSimplificationApplied.
   * Usado por el panel de Exportación (M1-S10) para poder ofrecer la versión
   * dimensionada como una tercera fuente de descarga, además del vector y la
   * simplificación. Opcional.
   */
  onDimensionApplied?: (dimension: DimensionResponse) => void;
}

const STATUS_LABEL: Record<DimensionStatus, string> = {
  idle: "",
  applying: "Aplicando…",
  applied: "Dimensiones aplicadas.",
  error: "",
};

/**
 * Orquesta el flujo de "Usuario podrá" de spec.md M1-S09: definir ancho o
 * alto en mm, bloquear/desbloquear proporción y ver el tamaño final antes de
 * aplicar. El preview se calcula 100% en el cliente (ver useDimensions) --
 * "Aplicar" es la única llamada a la Web API, y persiste el resultado como
 * una nueva versión (nunca sobrescribe la anterior, ver
 * Vectify.Api.Dimensioning.DimensionVersion).
 */
export function DimensionPanel({ projectId, imageId, sources, onDimensionApplied }: DimensionPanelProps) {
  const [selectedIndex, setSelectedIndex] = useState(sources.length - 1);
  const selectedSource = sources[selectedIndex] ?? sources[0];

  const {
    widthInput,
    heightInput,
    lockAspectRatio,
    preview,
    status,
    applied,
    errorMessage,
    setWidthInput,
    setHeightInput,
    setLockAspectRatio,
    apply,
  } = useDimensions(
    projectId,
    imageId,
    selectedSource.kind,
    selectedSource.id,
    selectedSource.widthPx,
    selectedSource.heightPx,
  );

  useEffect(() => {
    if (status === "applied" && applied) {
      onDimensionApplied?.(applied);
    }
  }, [status, applied, onDimensionApplied]);

  const isApplying = status === "applying";
  const appliedSvgUrl = applied ? getDimensionedSvgUrl(projectId, imageId, applied.dimensionId) : null;

  return (
    <div className="dimension-panel">
      {sources.length > 1 && (
        <fieldset className="dimension-panel__source">
          <legend className="dimension-panel__source-legend">Fuente a dimensionar</legend>
          <div className="dimension-panel__source-options" role="radiogroup" aria-label="Fuente a dimensionar">
            {sources.map((source, index) => (
              <label key={source.id} className="dimension-panel__source-option">
                <input
                  type="radio"
                  name="dimension-source"
                  checked={selectedIndex === index}
                  onChange={() => setSelectedIndex(index)}
                  aria-label={source.label}
                />
                <span aria-hidden="true">{source.label}</span>
              </label>
            ))}
          </div>
        </fieldset>
      )}

      <DimensionControls
        widthInput={widthInput}
        heightInput={heightInput}
        lockAspectRatio={lockAspectRatio}
        disabled={isApplying}
        onWidthChange={setWidthInput}
        onHeightChange={setHeightInput}
        onLockChange={setLockAspectRatio}
      />

      {/* Sin role="status": este texto se recalcula en cada tecla que el
          usuario escribe (ver useDimensions), un live region anunciaría cada
          cambio y sería ruidoso para lectores de pantalla -- a diferencia de
          dimension-panel__status, que solo cambia en eventos discretos
          (aplicar/error). */}
      <p className="dimension-panel__preview">
        {preview.ok
          ? `Tamaño final: ${formatMm(preview.widthMm)}mm × ${formatMm(preview.heightMm)}mm.`
          : preview.reason}
      </p>

      <div className="dimension-panel__actions">
        <button
          type="button"
          className="upload-actions__button upload-actions__button--primary"
          onClick={apply}
          disabled={!preview.ok || isApplying}
        >
          {isApplying ? "Aplicando…" : "Aplicar"}
        </button>
        <span className="dimension-panel__status" role="status">
          {STATUS_LABEL[status]}
        </span>
      </div>

      {status === "error" && errorMessage && (
        <p className="upload-panel__error" role="alert">
          {errorMessage}
        </p>
      )}

      {status === "applied" && applied && appliedSvgUrl && (
        <p className="dimension-panel__hint">
          Dimensiones aplicadas como versión {applied.version}: {formatMm(applied.widthMm)}mm ×{" "}
          {formatMm(applied.heightMm)}mm.{" "}
          <a href={appliedSvgUrl} target="_blank" rel="noreferrer">
            Ver SVG con dimensiones físicas
          </a>
          .
        </p>
      )}
    </div>
  );
}

function formatMm(valueMm: number): string {
  return Number(valueMm.toFixed(3)).toString();
}
