import { getSimplificationSvgUrl } from "../../api/simplifyApi";
import { useSimplify } from "../../hooks/useSimplify";
import { svgToDataUrl } from "../../lib/svgToDataUrl";
import { SimplifyComparison } from "./SimplifyComparison";
import { SimplifyControls } from "./SimplifyControls";

interface SimplifyPanelProps {
  projectId: string;
  imageId: string;
  fileName: string;
  /** SVG YA vectorizado (M1-S05) sobre el que se simplifica. */
  sourceVectorId: string;
  currentVectorUrl: string;
  currentVectorWidth: number;
  currentVectorHeight: number;
}

const STATUS_LABEL: Record<string, string> = {
  idle: "",
  previewing: "Generando preview…",
  "preview-ready": "Preview listo. Revisá la comparación antes de aplicar.",
  applying: "Aplicando…",
  applied: "Simplificación aplicada.",
  error: "",
};

/**
 * Orquesta el flujo de "Usuario podrá" de spec.md M1-S07: elegir tolerancia/
 * preset, ver preview y contador antes/después, aplicar o cancelar. Opera
 * sobre un SVG ya vectorizado (M1-S05): etapa posterior del mismo pipeline.
 *
 * El preview NUNCA se persiste (ver useSimplify.requestPreview): el SVG
 * resultante viaja completo en la respuesta y se renderiza como data URL
 * (svgToDataUrl) -- "cancelar" es puramente descartar ese estado local, sin
 * llamada de red, porque no hay nada que deshacer del lado del servidor.
 * "Aplicar" sí persiste: crea una nueva versión (nunca sobrescribe la
 * anterior, ver Vectify.Api.Simplification.SimplificationVersion).
 */
export function SimplifyPanel({
  projectId,
  imageId,
  fileName,
  sourceVectorId,
  currentVectorUrl,
  currentVectorWidth,
  currentVectorHeight,
}: SimplifyPanelProps) {
  const { preset, status, preview, applied, errorMessage, setPreset, requestPreview, apply, cancel } = useSimplify(
    projectId,
    imageId,
    sourceVectorId,
  );

  const isPreviewing = status === "previewing";
  const isApplying = status === "applying";
  const hasPreview = status === "preview-ready" && preview !== null;
  const busy = isPreviewing || isApplying;

  const previewDataUrl = preview ? svgToDataUrl(preview.svg) : null;
  const appliedSvgUrl = applied ? getSimplificationSvgUrl(projectId, imageId, applied.simplificationId) : null;
  const metrics = preview?.metrics ?? applied?.metrics ?? null;

  return (
    <div className="simplify-panel">
      <SimplifyControls preset={preset} disabled={busy} onPresetChange={setPreset} />

      <div className="simplify-panel__actions">
        <button
          type="button"
          className="upload-actions__button upload-actions__button--primary"
          onClick={requestPreview}
          disabled={busy}
        >
          {isPreviewing ? "Generando preview…" : "Vista previa"}
        </button>

        {hasPreview && (
          <>
            <button
              type="button"
              className="upload-actions__button upload-actions__button--primary"
              onClick={apply}
              disabled={isApplying}
            >
              {isApplying ? "Aplicando…" : "Aplicar"}
            </button>
            <button type="button" className="upload-actions__button" onClick={cancel} disabled={isApplying}>
              Cancelar
            </button>
          </>
        )}

        <span className="simplify-panel__status" role="status">
          {STATUS_LABEL[status]}
        </span>
      </div>

      {status === "error" && errorMessage && (
        <p className="upload-panel__error" role="alert">
          {errorMessage}
        </p>
      )}

      {metrics && (
        <dl className="service-card__details simplify-panel__metrics">
          <div>
            <dt>Nodos antes</dt>
            <dd>{metrics.before.approxNodeCount}</dd>
          </div>
          <div>
            <dt>Nodos después</dt>
            <dd>{metrics.after.approxNodeCount}</dd>
          </div>
          <div>
            <dt>Reducción</dt>
            <dd>{metrics.reductionPercent.toFixed(1)}%</dd>
          </div>
        </dl>
      )}

      {hasPreview && preview && previewDataUrl && (
        <SimplifyComparison
          currentUrl={currentVectorUrl}
          currentAlt={`SVG vectorizado actual de ${fileName}`}
          currentWidth={currentVectorWidth}
          currentHeight={currentVectorHeight}
          previewUrl={previewDataUrl}
          previewAlt={`Preview simplificado de ${fileName}`}
          previewWidth={preview.width}
          previewHeight={preview.height}
        />
      )}

      {status === "applied" && applied && appliedSvgUrl && (
        <p className="simplify-panel__hint">
          Simplificación aplicada como versión {applied.version}.{" "}
          <a href={appliedSvgUrl} target="_blank" rel="noreferrer">
            Ver SVG simplificado
          </a>
          .
        </p>
      )}

      {status === "idle" && !preview && (
        <p className="simplify-panel__hint">
          Elegí una tolerancia y pulsá &quot;Vista previa&quot; para ver nodos antes/después antes de aplicar.
        </p>
      )}
    </div>
  );
}
