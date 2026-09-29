import { OPERATION_LABEL } from "./manufacturingOperationLabels";
import type { ConsolidatedVectorLayerPayload } from "../../types/consolidatedVectorLayers";
import type { ManufacturingOperationValue } from "../../types/manufacturingOperations";
import type { VectorLayerPayload } from "../../types/vectorLayers";

interface LayerInfoPanelProps {
  /** Capa actualmente seleccionada (selección COMPARTIDA con la paleta) -- null si ninguna. */
  selectedLayer: VectorLayerPayload | null;
  /** Info consolidada (M2.1-S03/M2.1-S04) de esa misma capa, si ya se pudo recuperar. */
  consolidated: ConsolidatedVectorLayerPayload | null;
  consolidatedStatus: "idle" | "loading" | "ready" | "error";
  consolidatedErrorMessage: string | null;
  onRefresh: () => void;
  isVisible: boolean;
  onIsolate: () => void;
  onShowAll: () => void;
}

/**
 * "Información visible por layer" de spec.md M2.1-S04: nombre, HEX, número
 * de paths, número de componentes físicos, visibilidad y operación de
 * fabricación (si ya existe) de la capa actualmente seleccionada --
 * `nombre`/`HEX`/`número de paths`/`número de componentes físicos`/
 * `operación de fabricación` se leen del endpoint CONSOLIDADO de M2.1-S03
 * (`consolidated`, ver useConsolidatedVectorLayers) como fuente única, en
 * vez de volver a combinarlos a mano acá; `visibilidad` es el mismo
 * `visibility` record de M2-S02 (ver LayersPanel).
 *
 * También aloja Isolate/Show All (M2.1-S04): un click muestra ÚNICAMENTE la
 * capa seleccionada (reutilizando el mecanismo de visibilidad ya existente,
 * ver useVectorLayers.isolate) o restaura todas.
 */
export function LayerInfoPanel({
  selectedLayer,
  consolidated,
  consolidatedStatus,
  consolidatedErrorMessage,
  onRefresh,
  isVisible,
  onIsolate,
  onShowAll,
}: LayerInfoPanelProps) {
  if (!selectedLayer) {
    return (
      <div className="layer-info-panel layer-info-panel--empty">
        <p className="layer-info-panel__hint">
          Seleccioná un color en la paleta o una capa en la lista para ver su información y aislarla.
        </p>
        <button type="button" className="upload-actions__button" onClick={onShowAll}>
          Mostrar todas
        </button>
      </div>
    );
  }

  const operation = (consolidated?.manufacturingOperation as ManufacturingOperationValue | undefined) ?? "unassigned";

  return (
    <div className="layer-info-panel" role="group" aria-label={`Información de la capa ${selectedLayer.name}`}>
      <div className="layer-info-panel__header">
        <span
          className="layer-row__color"
          style={{ backgroundColor: selectedLayer.colorHex }}
          aria-hidden="true"
          title={selectedLayer.colorHex}
        />
        <span className="layer-info-panel__title">{selectedLayer.name}</span>
      </div>

      <dl className="service-card__details layer-info-panel__details">
        <div>
          <dt>HEX</dt>
          <dd>{selectedLayer.colorHex}</dd>
        </div>
        <div>
          <dt>Paths</dt>
          <dd>{consolidated ? consolidated.pathCount : "—"}</dd>
        </div>
        <div>
          <dt>Componentes físicos</dt>
          <dd>{consolidated?.componentCount ?? "Sin calcular"}</dd>
        </div>
        <div>
          <dt>Visibilidad</dt>
          <dd>{isVisible ? "Visible" : "Oculta"}</dd>
        </div>
        <div>
          <dt>Operación de fabricación</dt>
          <dd>{OPERATION_LABEL[operation]}</dd>
        </div>
      </dl>

      {consolidatedStatus === "loading" && <p className="layers-panel__status">Actualizando información…</p>}
      {consolidatedErrorMessage && (
        <p className="upload-panel__error" role="alert">
          {consolidatedErrorMessage}
        </p>
      )}

      <div className="layer-info-panel__actions">
        <button type="button" className="upload-actions__button upload-actions__button--primary" onClick={onIsolate}>
          Aislar
        </button>
        <button type="button" className="upload-actions__button" onClick={onShowAll}>
          Mostrar todas
        </button>
        <button type="button" className="upload-actions__button" onClick={onRefresh}>
          Actualizar info
        </button>
      </div>
    </div>
  );
}
