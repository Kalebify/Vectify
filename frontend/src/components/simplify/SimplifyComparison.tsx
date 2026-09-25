import { useEffect, useRef, useState } from "react";
import { useCanvasTransform, type ContainerSize } from "../../hooks/useCanvasTransform";
import { VectorCanvas } from "../vectorize/VectorCanvas";

interface SimplifyComparisonProps {
  currentUrl: string;
  currentAlt: string;
  currentWidth: number;
  currentHeight: number;
  previewUrl: string;
  previewAlt: string;
  previewWidth: number;
  previewHeight: number;
}

const MIN_SCALE = 0.1;
const MAX_SCALE = 8;
const ZOOM_BUTTON_FACTOR = 1.25;
const SCALE_EPSILON = 1e-6;

/**
 * Comparación lado a lado del vector actual contra el preview de
 * simplificación (spec.md M1-S07: "ver un preview ... antes de aplicar o
 * cancelar"). Reutiliza useCanvasTransform/VectorCanvas (M1-S06) para
 * zoom/pan/fit-to-screen compartido entre ambos paneles -- mismo patrón que
 * VectorComparison, pero con las etiquetas propias de esta etapa ("Vector
 * actual"/"Preview simplificado") en vez de "Original"/"SVG vectorizado": se
 * mantiene como componente propio (no una reutilización directa de
 * VectorComparison) para no acoplar el copy de M1-S06 a esta etapa ni
 * arriesgar sus tests existentes.
 */
export function SimplifyComparison({
  currentUrl,
  currentAlt,
  currentWidth,
  currentHeight,
  previewUrl,
  previewAlt,
  previewWidth,
  previewHeight,
}: SimplifyComparisonProps) {
  const { transform, minScale, maxScale, zoomBy, panBy, reset, fitToScreen } = useCanvasTransform({
    minScale: MIN_SCALE,
    maxScale: MAX_SCALE,
  });

  const [previewContainerSize, setPreviewContainerSize] = useState<ContainerSize | null>(null);
  const hasAutoFitted = useRef(false);

  useEffect(() => {
    if (hasAutoFitted.current || !previewContainerSize) {
      return;
    }
    hasAutoFitted.current = true;
    fitToScreen(previewContainerSize, { width: previewWidth, height: previewHeight });
  }, [previewContainerSize, previewWidth, previewHeight, fitToScreen]);

  const handleFitToScreen = () => {
    if (previewContainerSize) {
      fitToScreen(previewContainerSize, { width: previewWidth, height: previewHeight });
    }
  };

  const zoomPercent = Math.round(transform.scale * 100);
  const atMinScale = transform.scale <= minScale + SCALE_EPSILON;
  const atMaxScale = transform.scale >= maxScale - SCALE_EPSILON;

  return (
    <div className="vector-comparison simplify-comparison">
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
        <button type="button" className="upload-actions__button" onClick={handleFitToScreen}>
          Ajustar a pantalla
        </button>
      </div>

      <div className="vector-comparison__panes">
        <figure className="vector-comparison__pane">
          <VectorCanvas
            label="Vector actual"
            src={currentUrl}
            alt={currentAlt}
            intrinsicWidth={currentWidth}
            intrinsicHeight={currentHeight}
            transform={transform}
            onZoomBy={zoomBy}
            onPanBy={panBy}
          />
          <figcaption>Vector actual</figcaption>
        </figure>

        <figure className="vector-comparison__pane">
          <VectorCanvas
            label="Preview simplificado"
            src={previewUrl}
            alt={previewAlt}
            intrinsicWidth={previewWidth}
            intrinsicHeight={previewHeight}
            transform={transform}
            onZoomBy={zoomBy}
            onPanBy={panBy}
            onMeasure={setPreviewContainerSize}
          />
          <figcaption>Preview simplificado</figcaption>
        </figure>
      </div>
    </div>
  );
}
