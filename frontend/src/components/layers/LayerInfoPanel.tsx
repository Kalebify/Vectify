import { useState } from "react";
import { OPERATION_LABEL } from "./manufacturingOperationLabels";
import type { ConsolidatedVectorLayerPayload } from "../../types/consolidatedVectorLayers";
import type { ManufacturingOperationChoice, ManufacturingOperationValue } from "../../types/manufacturingOperations";
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
  /**
   * Rename inline (M2.1-S07, ronda de fix): OPCIONAL A PROPÓSITO. El flujo
   * clásico (`LayersPanel.tsx`) NO la pasa -- ahí renombrar sigue siendo
   * responsabilidad exclusiva de `ColorSwatchList` (ver su propio
   * comentario), así que sin esta prop el título se sigue mostrando como el
   * `<span>` de solo lectura de siempre. El Workspace (`InspectorPanel.tsx`)
   * SÍ la provee -- ver IMPL.md, "Ronda de fix 1" para la limitación
   * conocida del backend (`ColorPaletteService.RenameAsync` rechaza
   * renombrar una vez que la paleta está confirmada, y el Workspace SIEMPRE
   * opera sobre una paleta confirmada).
   */
  onRename?: (groupId: string, name: string) => void;
  /** Deshabilita el input de rename (Lock, M2.1-S07) -- ignorado si `onRename` no se provee. */
  renameDisabled?: boolean;
  /**
   * Cambio de operación de fabricación inline (M2.1-S07, ronda de fix) --
   * mismo criterio opcional que `onRename` arriba: sin esta prop, la
   * operación se sigue mostrando como el `<dd>` de solo lectura de siempre
   * (el flujo clásico ya tiene su propio selector por fila en `LayerList`).
   */
  onChangeOperation?: (groupId: string, operation: ManufacturingOperationChoice) => void;
  /** Deshabilita el selector de operación mientras la request está en curso, o si la capa está bloqueada (mismo valor combinado que decide el caller). */
  operationDisabled?: boolean;
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
  onRename,
  renameDisabled,
  onChangeOperation,
  operationDisabled,
}: LayerInfoPanelProps) {
  // Edición local del nombre en borrador (mismo patrón que ColorSwatchRow en
  // ColorSwatchList.tsx), resincronizada durante el render cuando cambia la
  // capa seleccionada o su nombre "real" cambió por fuera -- DEBE declararse
  // antes del return temprano de abajo (reglas de Hooks).
  const syncKey = selectedLayer ? `${selectedLayer.groupId}:${selectedLayer.name}` : null;
  const [draftName, setDraftName] = useState(selectedLayer?.name ?? "");
  const [lastSyncedKey, setLastSyncedKey] = useState<string | null>(syncKey);
  if (syncKey !== lastSyncedKey) {
    setLastSyncedKey(syncKey);
    setDraftName(selectedLayer?.name ?? "");
  }

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

  const commitRename = () => {
    if (!onRename) return;
    const trimmed = draftName.trim();
    if (trimmed && trimmed !== selectedLayer.name) {
      onRename(selectedLayer.groupId, trimmed);
    } else {
      setDraftName(selectedLayer.name);
    }
  };

  return (
    <div className="layer-info-panel" role="group" aria-label={`Información de la capa ${selectedLayer.name}`}>
      <div className="layer-info-panel__header">
        <span
          className="layer-row__color"
          style={{ backgroundColor: selectedLayer.colorHex }}
          aria-hidden="true"
          title={selectedLayer.colorHex}
        />
        {onRename ? (
          <input
            type="text"
            className="layer-info-panel__title-input"
            value={draftName}
            disabled={renameDisabled}
            title={renameDisabled ? "Capa bloqueada: desbloqueala para renombrarla" : undefined}
            onChange={(event) => setDraftName(event.target.value)}
            onBlur={commitRename}
            onKeyDown={(event) => {
              if (event.key === "Enter") {
                event.currentTarget.blur();
              }
            }}
            aria-label={`Nombre de la capa ${selectedLayer.name}`}
          />
        ) : (
          <span className="layer-info-panel__title">{selectedLayer.name}</span>
        )}
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
          <dd>
            {onChangeOperation ? (
              <select
                aria-label={`Operación de fabricación de la capa ${selectedLayer.name}`}
                value={operation}
                disabled={operationDisabled}
                title={operationDisabled ? "Capa bloqueada: desbloqueala para cambiar su operación" : undefined}
                onChange={(event) => onChangeOperation(selectedLayer.groupId, event.target.value as ManufacturingOperationChoice)}
              >
                <option value="unassigned" disabled hidden>
                  Sin asignar
                </option>
                <option value="cut">Corte</option>
                <option value="engrave">Grabado</option>
                <option value="ignore">Ignorar</option>
              </select>
            ) : (
              OPERATION_LABEL[operation]
            )}
          </dd>
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
