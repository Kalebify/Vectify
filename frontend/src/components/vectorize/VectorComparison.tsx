import { useEffect, useRef, useState } from "react";
import { useCanvasTransform, type ContainerSize } from "../../hooks/useCanvasTransform";
import { VectorCanvas } from "./VectorCanvas";

interface VectorComparisonProps {
  originalUrl: string;
  originalAlt: string;
  originalWidth: number | null;
  originalHeight: number | null;
  vectorUrl: string;
  vectorAlt: string;
  vectorWidth: number;
  vectorHeight: number;
  /** Ver criterio de "diseño grande" documentado en VectorizePanel.tsx. */
  maxScale: number;
  largeDesignNote: string | null;
}

const MIN_SCALE = 0.1;
const ZOOM_BUTTON_FACTOR = 1.25;
const SCALE_EPSILON = 1e-6;

/**
 * Comparación lado a lado de original y vector (spec.md M1-S06, "Usuario
 * podrá: ... comparar original/vector mediante lado a lado o slider" --
 * se eligió lado a lado, ver justificación en VectorizePanel.tsx). Una sola
 * instancia de useCanvasTransform compartida por ambos VectorCanvas: mover
 * el zoom/pan en cualquiera de los dos paneles mueve el otro exactamente
 * igual, que es la forma más directa de garantizar la "misma escala de
 * referencia" pedida por la Definition of Done sin tener que sincronizar dos
 * estados independientes.
 *
 * Se remonta (key={vector.vectorId} en VectorizePanel) cada vez que hay un
 * SVG nuevo, así que el ajuste automático a pantalla al montar siempre
 * refleja el resultado actual, sin arrastrar el zoom/pan de una
 * vectorización anterior.
 */
export function VectorComparison({
  originalUrl,
  originalAlt,
  originalWidth,
  originalHeight,
  vectorUrl,
  vectorAlt,
  vectorWidth,
  vectorHeight,
  maxScale,
  largeDesignNote,
}: VectorComparisonProps) {
  const { transform, minScale, zoomBy, panBy, reset, fitToScreen } = useCanvasTransform({
    minScale: MIN_SCALE,
    maxScale,
  });

  const [vectorContainerSize, setVectorContainerSize] = useState<ContainerSize | null>(null);
  const hasAutoFitted = useRef(false);

  // Ajuste inicial a pantalla: espera a la primera medición real del
  // contenedor del panel "vector" (ResizeObserver, asíncrona tras el mount)
  // y corre una única vez -- si corriera en cada medición, un resize de
  // ventana posterior pisaría el zoom/pan que el usuario ya haya elegido.
  useEffect(() => {
    if (hasAutoFitted.current || !vectorContainerSize) {
      return;
    }
    hasAutoFitted.current = true;
    fitToScreen(vectorContainerSize, { width: vectorWidth, height: vectorHeight });
  }, [vectorContainerSize, vectorWidth, vectorHeight, fitToScreen]);

  const handleFitToScreen = () => {
    if (vectorContainerSize) {
      fitToScreen(vectorContainerSize, { width: vectorWidth, height: vectorHeight });
    }
  };

  const zoomPercent = Math.round(transform.scale * 100);
  const atMinScale = transform.scale <= minScale + SCALE_EPSILON;
  const atMaxScale = transform.scale >= maxScale - SCALE_EPSILON;

  return (
    <div className="vector-comparison">
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

      {largeDesignNote && <p className="vector-comparison__note">{largeDesignNote}</p>}

      <div className="vector-comparison__panes">
        <figure className="vector-comparison__pane">
          <VectorCanvas
            label="Original"
            src={originalUrl}
            alt={originalAlt}
            intrinsicWidth={originalWidth}
            intrinsicHeight={originalHeight}
            transform={transform}
            onZoomBy={zoomBy}
            onPanBy={panBy}
          />
          <figcaption>Original</figcaption>
        </figure>

        <figure className="vector-comparison__pane">
          <VectorCanvas
            label="SVG vectorizado"
            src={vectorUrl}
            alt={vectorAlt}
            intrinsicWidth={vectorWidth}
            intrinsicHeight={vectorHeight}
            transform={transform}
            onZoomBy={zoomBy}
            onPanBy={panBy}
            onMeasure={setVectorContainerSize}
          />
          <figcaption>SVG vectorizado</figcaption>
        </figure>
      </div>
    </div>
  );
}
