import { getVectorSvgUrl } from "../../api/vectorizeApi";
import { useVectorize } from "../../hooks/useVectorize";

interface VectorizePanelProps {
  projectId: string;
  imageId: string;
  fileName: string;
  sourceMaskId: string;
  sourceMaskUrl: string;
  sourceWidth: number | null;
  sourceHeight: number | null;
}

const STATUS_LABEL: Record<string, string> = {
  idle: "",
  processing: "Vectorizando…",
  success: "Vectorización completa.",
  error: "",
};

/**
 * Orquesta el flujo de "Usuario podrá" de spec.md M1-S05: pulsar
 * "Vectorizar", ver processing/success/error, y recibir un SVG renderizable
 * asociado al proyecto. Opera sobre la máscara B/N ya generada (M1-S04): es
 * la etapa siguiente del mismo pipeline. El SVG se muestra con un <img>
 * (no inline vía dangerouslySetInnerHTML): aunque la Web API ya sanitiza el
 * SVG del lado de Python, renderizarlo como imagen es una capa adicional de
 * defensa en profundidad (el navegador nunca ejecuta script embebido dentro
 * de un <img>, a diferencia de SVG inline en el DOM).
 */
export function VectorizePanel({
  projectId,
  imageId,
  fileName,
  sourceMaskId,
  sourceMaskUrl,
  sourceWidth,
  sourceHeight,
}: VectorizePanelProps) {
  const { status, vector, errorMessage, vectorize } = useVectorize(projectId, imageId, sourceMaskId);

  const svgUrl = vector ? getVectorSvgUrl(projectId, imageId, vector.vectorId) : null;
  const isProcessing = status === "processing";

  return (
    <div className="vectorize-panel">
      <div className="vectorize-panel__actions">
        <button
          type="button"
          className="upload-actions__button upload-actions__button--primary"
          onClick={vectorize}
          disabled={isProcessing}
        >
          {isProcessing ? "Vectorizando…" : status === "success" ? "Vectorizar de nuevo" : "Vectorizar"}
        </button>
        <span className="vectorize-panel__status" role="status">
          {STATUS_LABEL[status]}
        </span>
      </div>

      {status === "error" && errorMessage && (
        <p className="upload-panel__error" role="alert">
          {errorMessage}
        </p>
      )}

      <div className="vectorize-comparison">
        <figure className="vectorize-comparison__item">
          {sourceWidth && sourceHeight ? (
            <img
              src={sourceMaskUrl}
              alt={`Máscara binaria de ${fileName}`}
              width={sourceWidth}
              height={sourceHeight}
              loading="lazy"
            />
          ) : (
            <img src={sourceMaskUrl} alt={`Máscara binaria de ${fileName}`} loading="lazy" />
          )}
          <figcaption>Máscara de origen</figcaption>
        </figure>

        <figure className="vectorize-comparison__item">
          {svgUrl ? (
            <img
              src={svgUrl}
              alt={`SVG vectorizado de ${fileName}`}
              width={vector!.width}
              height={vector!.height}
              loading="lazy"
            />
          ) : (
            <div
              className="vectorize-comparison__placeholder"
              style={{ width: sourceWidth ?? 240, height: sourceHeight ?? 180 }}
              role="img"
              aria-label={isProcessing ? "Vectorizando…" : "Todavía no se generó ningún SVG"}
            />
          )}
          <figcaption>SVG resultante</figcaption>
        </figure>
      </div>

      {vector && (
        <dl className="service-card__details vectorize-panel__metrics">
          <div>
            <dt>Versión del vector</dt>
            <dd>{vector.version}</dd>
          </div>
          <div>
            <dt>Paths</dt>
            <dd>{vector.metrics.pathCount}</dd>
          </div>
          <div>
            <dt>Nodos aproximados</dt>
            <dd>{vector.metrics.approxNodeCount}</dd>
          </div>
          <div>
            <dt>Bounds</dt>
            <dd>
              {vector.metrics.bounds.width.toFixed(1)} × {vector.metrics.bounds.height.toFixed(1)}
            </dd>
          </div>
        </dl>
      )}
    </div>
  );
}
