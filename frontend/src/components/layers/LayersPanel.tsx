import { getVectorLayerSvgUrl } from "../../api/vectorLayersApi";
import { useLayerComponents } from "../../hooks/useLayerComponents";
import { useVectorLayers } from "../../hooks/useVectorLayers";
import { ComponentTree } from "./ComponentTree";
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

const COMPONENTS_STATUS_LABEL: Record<string, string> = {
  idle: "",
  loading: "Calculando componentes físicos por capa…",
  ready: "Componentes calculados: seleccioná una pieza en la lista o en el canvas para localizarla.",
  error: "",
};

function formatUnits(value: number): string {
  return Math.round(value).toLocaleString("es-AR");
}

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

  const {
    status: componentsStatus,
    errorMessage: componentsErrorMessage,
    componentsByGroup,
    selected: selectedComponent,
    compute: computeComponents,
    select: selectComponent,
  } = useLayerComponents(projectId, imageId, layerSet?.layers ?? []);

  const isBusy = status === "generating";
  const isComputingComponents = componentsStatus === "loading";

  const selectedLayer = selectedComponent && layerSet?.layers.find((layer) => layer.groupId === selectedComponent.groupId);
  const selectedComponentDetail =
    selectedComponent &&
    componentsByGroup[selectedComponent.groupId]?.find((component) => component.id === selectedComponent.componentId);

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
          <div className="layers-panel__sidebar">
            <LayerList layers={layerSet.layers} visibility={visibility} onToggleVisibility={toggleVisibility} />

            <div className="layers-panel__controls">
              <button
                type="button"
                className="upload-actions__button"
                onClick={computeComponents}
                disabled={isComputingComponents}
              >
                {Object.keys(componentsByGroup).length > 0 ? "Recalcular componentes" : "Calcular componentes"}
              </button>
              <span className="layers-panel__status" role="status">
                {COMPONENTS_STATUS_LABEL[componentsStatus]}
              </span>
            </div>

            {componentsErrorMessage && (
              <p className="upload-panel__error" role="alert">
                {componentsErrorMessage}
              </p>
            )}

            <ComponentTree
              layers={layerSet.layers}
              componentsByGroup={componentsByGroup}
              selected={selectedComponent}
              onSelect={selectComponent}
            />

            {selectedComponentDetail && selectedLayer && (
              <dl className="service-card__details component-details" aria-label="Métricas de la pieza seleccionada">
                <div>
                  <dt>Pieza seleccionada</dt>
                  <dd>{selectedLayer.name}</dd>
                </div>
                <div>
                  <dt>Área aproximada</dt>
                  <dd>{formatUnits(selectedComponentDetail.area)} u²</dd>
                </div>
                <div>
                  <dt>Bounds</dt>
                  <dd>
                    ({formatUnits(selectedComponentDetail.bounds.minX)}, {formatUnits(selectedComponentDetail.bounds.minY)}) –
                    ({formatUnits(selectedComponentDetail.bounds.maxX)}, {formatUnits(selectedComponentDetail.bounds.maxY)})
                  </dd>
                </div>
              </dl>
            )}
          </div>

          <LayerCanvas
            layers={layerSet.layers}
            visibility={visibility}
            sourceWidthPx={layerSet.sourceWidthPx}
            sourceHeightPx={layerSet.sourceHeightPx}
            getSvgUrl={(vectorId) => getVectorLayerSvgUrl(projectId, imageId, vectorId)}
            componentsByGroup={componentsByGroup}
            selected={selectedComponent}
            onSelectComponent={selectComponent}
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
