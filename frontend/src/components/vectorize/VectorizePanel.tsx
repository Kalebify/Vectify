import { getVectorSvgUrl } from "../../api/vectorizeApi";
import { useVectorize } from "../../hooks/useVectorize";
import { VectorComparison } from "./VectorComparison";

interface VectorizePanelProps {
  projectId: string;
  imageId: string;
  fileName: string;
  sourceMaskId: string;
  /** Original YA cargado (M1-S02), sin procesar: es el término de comparación de spec.md M1-S06 ("Original y vector pueden inspeccionarse con la misma escala de referencia"). */
  originalUrl: string;
  originalWidth: number | null;
  originalHeight: number | null;
}

const STATUS_LABEL: Record<string, string> = {
  idle: "",
  processing: "Vectorizando…",
  success: "Vectorización completa.",
  error: "",
};

/** Ver criterio de "diseño grande" documentado más abajo en getLargeDesignNote. */
const LARGE_PATH_COUNT = 500;
const LARGE_NODE_COUNT = 5000;
const LARGE_AREA_PX = 4_000_000; // ej. ~2000×2000
const DEFAULT_MAX_SCALE = 8;
const LARGE_DESIGN_MAX_SCALE = 4;

/**
 * Criterio de "diseño grande" (spec.md M1-S06, no cuantificado en el spec --
 * supuesto declarado): un SVG se considera grande si tiene muchos paths/
 * nodos (más carga de rasterizado del navegador al hacer zoom) o si sus
 * dimensiones son grandes en píxeles. En ese caso se reduce el zoom máximo
 * permitido (VectorComparison ya throttlea pan/zoom vía rAF siempre, para
 * todos los tamaños) y se avisa al usuario. Los umbrales son heurísticos, no
 * medidos contra un dataset real: 500 paths / 5000 nodos aproximados / ~4
 * millones de px² (~2000×2000).
 */
function getLargeDesignNote(pathCount: number, approxNodeCount: number, width: number, height: number): string | null {
  const isLarge = pathCount > LARGE_PATH_COUNT || approxNodeCount > LARGE_NODE_COUNT || width * height > LARGE_AREA_PX;
  if (!isLarge) {
    return null;
  }
  return "Diseño grande: el zoom máximo se limitó para mantener la fluidez de la visualización.";
}

/**
 * Orquesta el flujo de "Usuario podrá" de spec.md M1-S05: pulsar
 * "Vectorizar", ver processing/success/error, y recibir un SVG renderizable
 * asociado al proyecto. Opera sobre la máscara B/N ya generada (M1-S04): es
 * la etapa siguiente del mismo pipeline.
 *
 * La comparación e inspección visual (M1-S06, "Usuario podrá: Ver SVG, zoom,
 * pan, fit-to-screen y comparar original/vector") se delega en
 * VectorComparison/VectorCanvas una vez que hay un vector listo. El SVG se
 * sigue mostrando con un <img> (no inline vía dangerouslySetInnerHTML):
 * aunque la Web API ya sanitiza el SVG del lado de Python, renderizarlo como
 * imagen es una capa adicional de defensa en profundidad (el navegador nunca
 * ejecuta script embebido dentro de un <img>, a diferencia de SVG inline en
 * el DOM) -- el zoom/pan con transform CSS sobre ese <img> alcanza para
 * cumplir M1-S06 sin necesitar acceso al DOM interno del SVG (que de todos
 * modos está fuera de alcance: no hay selección de nodos ni edición).
 */
export function VectorizePanel({
  projectId,
  imageId,
  fileName,
  sourceMaskId,
  originalUrl,
  originalWidth,
  originalHeight,
}: VectorizePanelProps) {
  const { status, vector, errorMessage, vectorize } = useVectorize(projectId, imageId, sourceMaskId);

  const svgUrl = vector ? getVectorSvgUrl(projectId, imageId, vector.vectorId) : null;
  const isProcessing = status === "processing";
  const largeDesignNote = vector
    ? getLargeDesignNote(vector.metrics.pathCount, vector.metrics.approxNodeCount, vector.width, vector.height)
    : null;

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

      {vector && svgUrl ? (
        <VectorComparison
          key={vector.vectorId}
          originalUrl={originalUrl}
          originalAlt={`Original de ${fileName}`}
          originalWidth={originalWidth}
          originalHeight={originalHeight}
          vectorUrl={svgUrl}
          vectorAlt={`SVG vectorizado de ${fileName}`}
          vectorWidth={vector.width}
          vectorHeight={vector.height}
          maxScale={largeDesignNote ? LARGE_DESIGN_MAX_SCALE : DEFAULT_MAX_SCALE}
          largeDesignNote={largeDesignNote}
        />
      ) : (
        <p className="vectorize-panel__hint">
          {isProcessing
            ? "Vectorizando…"
            : "Pulsá \"Vectorizar\" para habilitar la comparación original/vector con zoom y pan."}
        </p>
      )}

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
