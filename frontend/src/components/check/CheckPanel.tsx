import { useEffect, useRef, useState } from "react";
import { fetchSvgText } from "../../api/checkApi";
import { useCanvasTransform, type ContainerSize } from "../../hooks/useCanvasTransform";
import { useCheck, type CheckStatus } from "../../hooks/useCheck";
import { highlightSvgPath } from "../../lib/highlightSvgPath";
import { svgToDataUrl } from "../../lib/svgToDataUrl";
import type { CheckSourceKind } from "../../types/check";
import { VectorCanvas } from "../vectorize/VectorCanvas";
import { CheckIssueList, type IssueFilter } from "./CheckIssueList";

/** Un SVG ya generado (vectorizado o simplificado) que el usuario puede elegir como fuente del análisis (ver spec.md M1-S08, "no está definido si opera sobre VectorVersion o SimplificationVersion -- se aceptan ambas"). */
export interface CheckSourceOption {
  kind: CheckSourceKind;
  id: string;
  label: string;
  svgUrl: string;
  width: number;
  height: number;
}

interface CheckPanelProps {
  projectId: string;
  imageId: string;
  fileName: string;
  /** Al menos una fuente (el vector actual); una segunda opcional si ya hay una simplificación aplicada. */
  sources: CheckSourceOption[];
}

const MIN_SCALE = 0.1;
const MAX_SCALE = 8;
const ZOOM_BUTTON_FACTOR = 1.25;
const SCALE_EPSILON = 1e-6;

const STATUS_LABEL: Record<CheckStatus, string> = {
  idle: "",
  running: "Analizando…",
  ready: "Análisis completo.",
  error: "",
};

/**
 * Orquesta el primer Laser Checker (spec.md M1-S08): el usuario ejecuta el
 * análisis a pedido sobre un SVG ya generado, ve la cantidad de paths
 * abiertos/duplicados, y puede hacer click en un issue para resaltarlo
 * sobre el VectorCanvas (M1-S06). Puramente de lectura: nunca modifica el
 * SVG de origen ni persiste nada -- correr el análisis de nuevo siempre
 * vuelve a pedirle el resultado a la Web API (sin caché local).
 */
export function CheckPanel({ projectId, imageId, fileName, sources }: CheckPanelProps) {
  const [selectedIndex, setSelectedIndex] = useState(sources.length - 1);
  const selectedSource = sources[selectedIndex] ?? sources[0];

  const { status, result, errorMessage, run } = useCheck(projectId, imageId, selectedSource.kind, selectedSource.id);

  const [filter, setFilter] = useState<IssueFilter>("all");
  const [selectedIssueId, setSelectedIssueId] = useState<string | null>(null);
  const [sourceSvgText, setSourceSvgText] = useState<string | null>(null);

  const { transform, minScale, maxScale, zoomBy, panBy, reset, fitToScreen } = useCanvasTransform({
    minScale: MIN_SCALE,
    maxScale: MAX_SCALE,
  });
  const [containerSize, setContainerSize] = useState<ContainerSize | null>(null);
  const hasAutoFitted = useRef(false);

  // Cambiar de fuente invalida la selección/resaltado y el texto ya
  // descargado del SVG anterior -- ya no corresponden a lo que se está
  // mostrando. Se resetea durante el render (patrón recomendado de React
  // para "ajustar estado cuando cambia una prop"), no en un efecto: evita
  // el repintado en cascada de un setState síncrono dentro de useEffect.
  const sourceKey = `${selectedSource.kind}:${selectedSource.id}`;
  const [lastSourceKey, setLastSourceKey] = useState(sourceKey);
  if (sourceKey !== lastSourceKey) {
    setLastSourceKey(sourceKey);
    setSelectedIssueId(null);
    setSourceSvgText(null);
  }

  // El ref (a diferencia del estado de arriba) se resetea en un efecto, no
  // durante el render: React desaconseja leer/escribir `ref.current` en el
  // cuerpo del render (solo en efectos/manejadores de evento).
  useEffect(() => {
    hasAutoFitted.current = false;
  }, [sourceKey]);

  // Tras un análisis listo, se descarga el texto del SVG UNA vez (no en cada
  // click de un issue) para poder resaltar localmente sin volver a pedirlo a
  // cada selección -- ver lib/highlightSvgPath.
  useEffect(() => {
    if (status !== "ready") {
      return;
    }

    let cancelled = false;
    fetchSvgText(selectedSource.svgUrl)
      .then((text) => {
        if (!cancelled) {
          setSourceSvgText(text);
        }
      })
      .catch(() => {
        // Fail-safe: si la descarga falla, el canvas sigue mostrando el SVG
        // sin resaltar (vía la URL directa del servidor) -- ver canvasSrc.
      });

    return () => {
      cancelled = true;
    };
  }, [status, selectedSource.svgUrl]);

  useEffect(() => {
    if (hasAutoFitted.current || !containerSize) {
      return;
    }
    hasAutoFitted.current = true;
    fitToScreen(containerSize, { width: selectedSource.width, height: selectedSource.height });
  }, [containerSize, selectedSource.width, selectedSource.height, fitToScreen]);

  const selectedIssue = result?.issues.find((issue) => issue.id === selectedIssueId) ?? null;
  const highlightedPathIndices = !selectedIssue
    ? []
    : selectedIssue.type === "open_path"
      ? [selectedIssue.pathIndex]
      : selectedIssue.members.map((member) => member.pathIndex);

  const canvasSrc = sourceSvgText
    ? svgToDataUrl(
        highlightedPathIndices.length > 0 ? highlightSvgPath(sourceSvgText, highlightedPathIndices) : sourceSvgText,
      )
    : selectedSource.svgUrl;

  const handleSelectIssue = (issueId: string) => {
    // Click de nuevo sobre el mismo issue lo deselecciona (vuelve a mostrar
    // el SVG completo sin resaltar).
    setSelectedIssueId((current) => (current === issueId ? null : issueId));
  };

  const handleSourceChange = (index: number) => {
    setSelectedIndex(index);
  };

  const isRunning = status === "running";
  const zoomPercent = Math.round(transform.scale * 100);
  const atMinScale = transform.scale <= minScale + SCALE_EPSILON;
  const atMaxScale = transform.scale >= maxScale - SCALE_EPSILON;

  return (
    <div className="check-panel">
      {sources.length > 1 && (
        <fieldset className="check-panel__source">
          <legend className="check-panel__source-legend">Fuente a analizar</legend>
          <div className="check-panel__source-options" role="radiogroup" aria-label="Fuente a analizar">
            {sources.map((source, index) => (
              <label key={source.id} className="check-panel__source-option">
                <input
                  type="radio"
                  name="check-source"
                  checked={selectedIndex === index}
                  onChange={() => handleSourceChange(index)}
                  aria-label={source.label}
                />
                <span aria-hidden="true">{source.label}</span>
              </label>
            ))}
          </div>
        </fieldset>
      )}

      <div className="check-panel__actions">
        <button
          type="button"
          className="upload-actions__button upload-actions__button--primary"
          onClick={run}
          disabled={isRunning}
        >
          {isRunning ? "Analizando…" : status === "ready" ? "Analizar de nuevo" : "Analizar"}
        </button>
        <span className="check-panel__status" role="status">
          {STATUS_LABEL[status]}
        </span>
      </div>

      {status === "error" && errorMessage && (
        <p className="upload-panel__error" role="alert">
          {errorMessage}
        </p>
      )}

      {result && (
        <>
          <dl className="service-card__details check-panel__summary">
            <div>
              <dt>Paths abiertos</dt>
              <dd>{result.summary.openPathCount}</dd>
            </div>
            <div>
              <dt>Duplicados/casi-duplicados</dt>
              <dd>{result.summary.duplicateGroupCount}</dd>
            </div>
            {result.skippedPathCount > 0 && (
              <div>
                <dt>Paths omitidos (comandos no soportados)</dt>
                <dd>{result.skippedPathCount}</dd>
              </div>
            )}
          </dl>

          <CheckIssueList
            issues={result.issues}
            filter={filter}
            onFilterChange={setFilter}
            selectedIssueId={selectedIssueId}
            onSelectIssue={handleSelectIssue}
          />

          <div className="vector-comparison__toolbar" role="toolbar" aria-label="Controles de zoom y desplazamiento">
            <button
              type="button"
              className="upload-actions__button"
              onClick={() => zoomBy(1 / ZOOM_BUTTON_FACTOR)}
              disabled={atMinScale}
              aria-label="Alejar"
            >
              −
            </button>
            <span className="vector-comparison__zoom-readout" aria-live="polite">
              {zoomPercent}%
            </span>
            <button
              type="button"
              className="upload-actions__button"
              onClick={() => zoomBy(ZOOM_BUTTON_FACTOR)}
              disabled={atMaxScale}
              aria-label="Acercar"
            >
              +
            </button>
            <button type="button" className="upload-actions__button" onClick={reset}>
              Restablecer
            </button>
            <button
              type="button"
              className="upload-actions__button"
              onClick={() => containerSize && fitToScreen(containerSize, { width: selectedSource.width, height: selectedSource.height })}
            >
              Ajustar a pantalla
            </button>
          </div>

          <VectorCanvas
            label={selectedSource.label}
            src={canvasSrc}
            alt={`SVG ${selectedSource.label} de ${fileName}${selectedIssue ? ", con el problema seleccionado resaltado" : ""}`}
            intrinsicWidth={selectedSource.width}
            intrinsicHeight={selectedSource.height}
            transform={transform}
            onZoomBy={zoomBy}
            onPanBy={panBy}
            onMeasure={setContainerSize}
          />
        </>
      )}

      {status === "idle" && (
        <p className="check-panel__hint">
          Pulsá &quot;Analizar&quot; para detectar paths abiertos y duplicados/casi-duplicados. El análisis es de
          solo lectura: nunca modifica el SVG.
        </p>
      )}
    </div>
  );
}
