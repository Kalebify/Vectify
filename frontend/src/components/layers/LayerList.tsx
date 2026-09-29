import { OPERATION_LABEL } from "./manufacturingOperationLabels";
import type { ManufacturingOperationChoice, ManufacturingOperationPayload, ManufacturingOperationValue } from "../../types/manufacturingOperations";
import type { VectorLayerPayload } from "../../types/vectorLayers";

interface LayerListProps {
  layers: VectorLayerPayload[];
  visibility: Record<string, boolean>;
  onToggleVisibility: (groupId: string) => void;
  /** groupId -> intención de fabricación vigente (M2-S07) -- "unassigned" para las capas que nunca recibieron una asignación explícita. */
  operations: Record<string, ManufacturingOperationPayload>;
  onChangeOperation: (groupId: string, operation: ManufacturingOperationChoice) => void;
  /** groupId de la capa cuya operación se está guardando, para deshabilitar su selector mientras la request está en curso. */
  mutatingGroupId: string | null;
  /** Filtro vigente (M2-S07, "mostrar solo Corte"): "all" muestra todas las capas. */
  operationFilter: ManufacturingOperationValue | "all";
  /** Selección COMPARTIDA con la paleta (M2.1-S04) -- ver ColorSwatchList.selectedLayerGroupId. Opcionales: sin ellos, la lista funciona igual que antes de esta tarjeta. */
  selectedGroupId?: string | null;
  onSelectGroup?: (groupId: string) => void;
}

const OPERATION_BADGE_CLASS: Record<ManufacturingOperationValue, string> = {
  cut: "manufacturing-operation-badge manufacturing-operation-badge--cut",
  engrave: "manufacturing-operation-badge manufacturing-operation-badge--engrave",
  ignore: "manufacturing-operation-badge manufacturing-operation-badge--ignore",
  unassigned: "manufacturing-operation-badge manufacturing-operation-badge--unassigned",
};

function operationOf(operations: Record<string, ManufacturingOperationPayload>, groupId: string): ManufacturingOperationValue {
  return operations[groupId]?.operation ?? "unassigned";
}

/**
 * Lista de capas de spec.md M2-S02 ("React"): swatch de color, nombre
 * (heredado del grupo de color, solo lectura -- renombrar es una operación
 * de M2-S01 sobre la paleta, no de esta tarjeta), porcentaje de área
 * aproximada y toggle de visibilidad individual.
 *
 * Extiende M2-S07 ("selector por capa... en el panel Layers ya existente, no
 * un panel paralelo"): cada fila agrega el selector Corte/Grabado/Ignorar y
 * una leyenda visual (badge con texto + color distintivo, consistente con
 * `.color-swatch__badge`) de la operación vigente. `operationFilter` oculta
 * (sin desmontar del todo el resto del panel: solo esta lista) las filas que
 *
 * M2.1-S04: el swatch de color se convierte en un `<button>` (en vez de un
 * `<span>` decorativo) cuando se provee `onSelectGroup` -- click selecciona
 * esa capa en la selección COMPARTIDA con la paleta (mismo groupId que
 * ColorSwatchList), resaltando la fila y su swatch correspondiente. Sin
 * `onSelectGroup` (ej. tests que no la necesitan) el swatch sigue siendo el
 * `<span>` puramente decorativo de siempre.
 *
 * no matchean, para "mostrar solo Corte" sin tocar visibilidad/canvas.
 */
export function LayerList({
  layers,
  visibility,
  onToggleVisibility,
  operations,
  onChangeOperation,
  mutatingGroupId,
  operationFilter,
  selectedGroupId,
  onSelectGroup,
}: LayerListProps) {
  const visibleLayers =
    operationFilter === "all" ? layers : layers.filter((layer) => operationOf(operations, layer.groupId) === operationFilter);

  return (
    <ul className="layer-list" aria-label="Capas vectoriales por color">
      {visibleLayers.length === 0 && (
        <li className="layer-list__empty">Ninguna capa tiene la operación de fabricación seleccionada en el filtro.</li>
      )}

      {visibleLayers.map((layer) => {
        const isVisible = visibility[layer.groupId] ?? true;
        const operation = operationOf(operations, layer.groupId);
        const isMutating = mutatingGroupId === layer.groupId;
        const isSelected = selectedGroupId === layer.groupId;

        return (
          <li
            key={layer.groupId}
            className={`layer-row${isSelected ? " layer-row--selected" : ""}`}
            aria-label={`Capa ${layer.name}`}
          >
            {onSelectGroup ? (
              <button
                type="button"
                className="layer-row__color layer-row__color--pick"
                style={{ backgroundColor: layer.colorHex }}
                aria-pressed={isSelected}
                aria-label={`Seleccionar la capa ${layer.name} (la resalta en la paleta de colores)`}
                onClick={() => onSelectGroup(layer.groupId)}
              />
            ) : (
              <span
                className="layer-row__color"
                style={{ backgroundColor: layer.colorHex }}
                aria-hidden="true"
                title={layer.colorHex}
              />
            )}
            <span className="layer-row__name">{layer.name}</span>
            <span className="layer-row__area">{layer.areaPercent.toFixed(1)}%</span>

            {layer.hasPartialAlpha && (
              <span className="color-swatch__badge" title="Parte de este grupo tiene transparencia parcial">
                semi-transparente
              </span>
            )}

            <span className={OPERATION_BADGE_CLASS[operation]}>{OPERATION_LABEL[operation]}</span>

            <label className="layer-row__operation">
              <select
                aria-label={`Operación de fabricación de la capa ${layer.name}`}
                value={operation}
                disabled={isMutating}
                onChange={(event) => onChangeOperation(layer.groupId, event.target.value as ManufacturingOperationChoice)}
              >
                <option value="unassigned" disabled hidden>
                  Sin asignar
                </option>
                <option value="cut">Corte</option>
                <option value="engrave">Grabado</option>
                <option value="ignore">Ignorar</option>
              </select>
            </label>

            <label className="layer-row__visibility">
              <input
                type="checkbox"
                checked={isVisible}
                onChange={() => onToggleVisibility(layer.groupId)}
                aria-label={`${isVisible ? "Ocultar" : "Mostrar"} la capa ${layer.name}`}
              />
              Visible
            </label>
          </li>
        );
      })}
    </ul>
  );
}
