import { getVectorLayerSvgUrl } from "../../api/vectorLayersApi";
import { useComponentGroups } from "../../hooks/useComponentGroups";
import { useExplodedView } from "../../hooks/useExplodedView";
import { useLayerComponents } from "../../hooks/useLayerComponents";
import { useVectorLayers } from "../../hooks/useVectorLayers";
import { ComponentTree } from "./ComponentTree";
import { ExplodedLegend } from "./ExplodedLegend";
import { ExplodedViewControls } from "./ExplodedViewControls";
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
 *
 * También orquesta spec.md M2-S04 ("vista explotada"): `useExplodedView`
 * decide únicamente CÓMO se dibuja el mismo LayerCanvas (ensamblada o
 * explotada) -- `visibility` (M2-S02) y `componentsByGroup`/`selected`
 * (M2-S03) son exactamente los mismos objetos en ambos modos, así que
 * aislar/ocultar un color y la selección de un componente ya son
 * consistentes entre las dos vistas sin ningún código adicional.
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

  const {
    groupsByLayer,
    selection: groupSelection,
    highlighted: highlightedGroup,
    errorMessage: groupsErrorMessage,
    mutatingLayerGroupId,
    toggleComponentSelection,
    createGroup,
    ungroup: ungroupComponents,
    rename: renameComponentGroup,
    selectGroupAsSet,
  } = useComponentGroups(projectId, imageId, layerSet?.layers ?? [], componentsByGroup);

  const { viewMode, separationPercent, setViewMode, setSeparationPercent } = useExplodedView();

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

            {groupsErrorMessage && (
              <p className="upload-panel__error" role="alert">
                {groupsErrorMessage}
              </p>
            )}

            <ComponentTree
              layers={layerSet.layers}
              componentsByGroup={componentsByGroup}
              selected={selectedComponent}
              onSelect={selectComponent}
              groupsByLayer={groupsByLayer}
              selection={groupSelection}
              onToggleComponentSelection={toggleComponentSelection}
              onCreateGroup={createGroup}
              mutatingLayerGroupId={mutatingLayerGroupId}
              highlightedGroupId={highlightedGroup?.groupId ?? null}
              onSelectGroupAsSet={selectGroupAsSet}
              onUngroup={ungroupComponents}
              onRenameGroup={renameComponentGroup}
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

          <div className="layers-panel__canvas-area">
            <ExplodedViewControls
              viewMode={viewMode}
              onChangeViewMode={setViewMode}
              separationPercent={separationPercent}
              onChangeSeparationPercent={setSeparationPercent}
            />

            <LayerCanvas
              layers={layerSet.layers}
              visibility={visibility}
              sourceWidthPx={layerSet.sourceWidthPx}
              sourceHeightPx={layerSet.sourceHeightPx}
              getSvgUrl={(vectorId) => getVectorLayerSvgUrl(projectId, imageId, vectorId)}
              componentsByGroup={componentsByGroup}
              selected={selectedComponent}
              onSelectComponent={selectComponent}
              highlightedGroup={highlightedGroup}
              exploded={viewMode === "exploded"}
              separationPercent={separationPercent}
            />

            {viewMode === "exploded" && (
              <ExplodedLegend layers={layerSet.layers} componentsByGroup={componentsByGroup} />
            )}
          </div>
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
