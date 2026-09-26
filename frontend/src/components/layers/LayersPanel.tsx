import { getVectorLayerSvgUrl } from "../../api/vectorLayersApi";
import { useVectorLayers } from "../../hooks/useVectorLayers";
import { LayerCanvas } from "./LayerCanvas";
import { LayerList } from "./LayerList";

interface LayersPanelProps {
  projectId: string;
  imageId: string;
  /** Sesión de paleta YA CONFIRMADA (M2-S01) -- precondición de esta tarjeta. */
  paletteId: string;
}

const STATUS_LABEL: Record<string, string> = {
  idle: "",
  generating: "Generando capas vectoriales (una por color)…",
  ready: "Capas generadas: aislá, ocultá o mostralas combinadas en el canvas.",
  error: "",
};

/**
 * Orquesta el flujo de "Usuario podrá" de spec.md M2-S02: ver una capa por
 * color, aislarla, ocultarla y comprobar qué geometría pertenece a ella.
 * Consume la paleta CONFIRMADA de M2-S01 (paletteId) -- no vuelve a mostrar
 * ni editar los grupos de color en sí, eso es responsabilidad exclusiva de
 * ColorPalettePanel.
 */
export function LayersPanel({ projectId, imageId, paletteId }: LayersPanelProps) {
  const { status, layerSet, visibility, errorMessage, generate, toggleVisibility } = useVectorLayers(
    projectId,
    imageId,
    paletteId,
  );

  const isBusy = status === "generating";

  return (
    <div className="layers-panel">
      <div className="layers-panel__controls">
        <button
          type="button"
          className="upload-actions__button upload-actions__button--primary"
          onClick={generate}
          disabled={isBusy}
        >
          {layerSet ? "Regenerar capas" : "Generar capas"}
        </button>
        <span className="layers-panel__status" role="status">
          {STATUS_LABEL[status]}
        </span>
      </div>

      {status === "error" && errorMessage && (
        <p className="upload-panel__error" role="alert">
          {errorMessage}
        </p>
      )}

      {layerSet && (
        <div className="layers-panel__body">
          <LayerList layers={layerSet.layers} visibility={visibility} onToggleVisibility={toggleVisibility} />
          <LayerCanvas
            layers={layerSet.layers}
            visibility={visibility}
            sourceWidthPx={layerSet.sourceWidthPx}
            sourceHeightPx={layerSet.sourceHeightPx}
            getSvgUrl={(vectorId) => getVectorLayerSvgUrl(projectId, imageId, vectorId)}
          />
        </div>
      )}

      {status === "idle" && (
        <p className="layers-panel__hint">
          Pulsá &quot;Generar capas&quot; para convertir cada color confirmado en una capa vectorial independiente y
          alineada, reutilizando el mismo motor de trazado que ya vectorizó el diseño completo.
        </p>
      )}
    </div>
  );
}
