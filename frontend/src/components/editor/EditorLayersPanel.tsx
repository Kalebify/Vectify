import { useState } from "react";
import type { DragEvent } from "react";
import type { VectorDocumentLayer } from "../../hooks/useVectorDocument";
import { OPERATION_LABEL } from "../layers/manufacturingOperationLabels";

interface EditorLayersPanelProps {
  layers: VectorDocumentLayer[];
  visibility: Record<string, boolean>;
  onToggleVisibility: (groupId: string) => void;
  onToggleLocked: (groupId: string) => void;
  /** Nuevo orden visual COMPLETO (Drag & Drop, M2.1-S07) -- ver useVectorDocument.reorderLayers. NUNCA toca geometría. */
  onReorder: (orderedGroupIds: string[]) => void;
  selectedGroupId: string | null;
  onSelectGroup: (groupId: string) => void;
}

/**
 * "LayersPanel" del wireframe obligatorio (columna derecha, sección LAYERS:
 * "👁 🔵 Blue 🔒", "+ ADD LAYER"). Spec.md: "ya existe, adaptar al panel
 * derecho" -- pero el `LayersPanel` YA EXISTENTE
 * (`components/layers/LayersPanel.tsx`) es el panel MONOLÍTICO de M2-S02..
 * M2.1-S04 (orquesta exploded view, component groups, physical union, modo
 * de comparación, SU PROPIO canvas de `<img>`...): montarlo tal cual acá
 * duplicaría el Canvas (el suyo, basado en `<img>`, contra el `VectorCanvas`
 * nuevo de Konva) y el Inspector (su `LayerInfoPanel` interno, contra
 * `InspectorPanel`). En vez de eso, este componente es la adaptación al
 * layout nuevo: reusa `LayerList`-como-patrón (mismo swatch/nombre/checkbox
 * de visibilidad) pero data-driven por `VectorDocument` (la fuente de
 * verdad ÚNICA de este sprint), consistente con "el estado de dominio no
 * debe quedar atrapado dentro de componentes visuales" (spec.md,
 * Arquitectura frontend). El componente monolítico anterior sigue existiendo
 * intacto y se sigue usando en el flujo clásico de App.tsx (ver IMPL.md,
 * "Ambigüedades resueltas").
 *
 * M2.1-S07: Lock (concepto NUEVO) deja de ser un ícono estático "llega en
 * MVP3" -- ahora es un toggle real y persistido (ver useVectorDocument.
 * toggleLocked), y cada fila es arrastrable (Drag & Drop nativo de HTML5,
 * sin agregar ninguna librería) para reordenar -- el nuevo orden se persiste
 * vía `onReorder`, NUNCA toca `d`/`transform`/geometría.
 *
 * "+ ADD LAYER" del wireframe queda deshabilitado: crear una capa nueva
 * desde cero (no derivada de un color detectado) es una herramienta de
 * edición real, fuera de alcance de esta tarjeta (spec.md, "Fuera de
 * alcance").
 */
export function EditorLayersPanel({
  layers,
  visibility,
  onToggleVisibility,
  onToggleLocked,
  onReorder,
  selectedGroupId,
  onSelectGroup,
}: EditorLayersPanelProps) {
  const [draggedGroupId, setDraggedGroupId] = useState<string | null>(null);
  const [dragOverGroupId, setDragOverGroupId] = useState<string | null>(null);

  const handleDragStart = (groupId: string) => (event: DragEvent<HTMLLIElement>) => {
    setDraggedGroupId(groupId);
    event.dataTransfer.effectAllowed = "move";
    event.dataTransfer.setData("text/plain", groupId);
  };

  const handleDragOver = (groupId: string) => (event: DragEvent<HTMLLIElement>) => {
    if (!draggedGroupId || draggedGroupId === groupId) return;
    event.preventDefault();
    event.dataTransfer.dropEffect = "move";
    setDragOverGroupId(groupId);
  };

  const handleDrop = (targetGroupId: string) => (event: DragEvent<HTMLLIElement>) => {
    event.preventDefault();
    const sourceGroupId = draggedGroupId ?? event.dataTransfer.getData("text/plain");
    setDraggedGroupId(null);
    setDragOverGroupId(null);
    if (!sourceGroupId || sourceGroupId === targetGroupId) return;

    const currentOrder = layers.map((layer) => layer.groupId);
    const withoutSource = currentOrder.filter((groupId) => groupId !== sourceGroupId);
    const targetIndex = withoutSource.indexOf(targetGroupId);
    if (targetIndex === -1) return;

    const nextOrder = [...withoutSource.slice(0, targetIndex), sourceGroupId, ...withoutSource.slice(targetIndex)];
    onReorder(nextOrder);
  };

  const handleDragEnd = () => {
    setDraggedGroupId(null);
    setDragOverGroupId(null);
  };

  return (
    <section aria-labelledby="editor-layers-heading" className="editor-layers-panel">
      <h3 id="editor-layers-heading" className="editor-panel__heading">
        Layers
      </h3>

      {layers.length === 0 ? (
        <p className="editor-panel__empty">Este proyecto todavía no tiene capas generadas.</p>
      ) : (
        <ul className="editor-layers-panel__list" aria-label="Capas del documento (arrastrá para reordenar)">
          {layers.map((layer) => {
            const isVisible = visibility[layer.groupId] ?? true;
            const isSelected = selectedGroupId === layer.groupId;
            const isLocked = layer.locked;

            return (
              <li
                key={layer.groupId}
                className={`editor-layers-panel__row${isSelected ? " editor-layers-panel__row--selected" : ""}${
                  dragOverGroupId === layer.groupId ? " editor-layers-panel__row--drag-over" : ""
                }`}
                draggable
                onDragStart={handleDragStart(layer.groupId)}
                onDragOver={handleDragOver(layer.groupId)}
                onDrop={handleDrop(layer.groupId)}
                onDragEnd={handleDragEnd}
              >
                <button
                  type="button"
                  className="editor-layers-panel__visibility"
                  aria-pressed={isVisible}
                  aria-label={`${isVisible ? "Ocultar" : "Mostrar"} la capa ${layer.name}`}
                  title={isVisible ? "Ocultar capa" : "Mostrar capa"}
                  onClick={() => onToggleVisibility(layer.groupId)}
                >
                  <span aria-hidden="true">{isVisible ? "\u{1F441}" : "\u{1F576}"}</span>
                </button>

                <button
                  type="button"
                  className="editor-layers-panel__select"
                  aria-pressed={isSelected}
                  aria-label={`Seleccionar la capa ${layer.name}`}
                  onClick={() => onSelectGroup(layer.groupId)}
                >
                  <span
                    className="editor-layers-panel__swatch"
                    style={{ backgroundColor: layer.colorHex }}
                    aria-hidden="true"
                    title={layer.colorHex}
                  />
                  <span className="editor-layers-panel__name">{layer.name}</span>
                  {isSelected && (
                    <span className="editor-layers-panel__selected-badge" aria-hidden="true">
                      ✓
                    </span>
                  )}
                </button>

                <span
                  className={`editor-layers-panel__operation editor-layers-panel__operation--${layer.manufacturingOperation}`}
                  title={`Operación de fabricación: ${OPERATION_LABEL[layer.manufacturingOperation]}`}
                >
                  {OPERATION_LABEL[layer.manufacturingOperation]}
                </span>

                <button
                  type="button"
                  className="editor-layers-panel__lock"
                  aria-pressed={isLocked}
                  aria-label={`${isLocked ? "Desbloquear" : "Bloquear"} la capa ${layer.name}`}
                  title={isLocked ? "Capa bloqueada: click para desbloquear" : "Bloquear capa (impide editar su geometría)"}
                  onClick={() => onToggleLocked(layer.groupId)}
                >
                  <span aria-hidden="true">{isLocked ? "\u{1F512}" : "\u{1F513}"}</span>
                </button>
              </li>
            );
          })}
        </ul>
      )}

      <button type="button" className="editor-layers-panel__add" disabled title="Agregar capa nueva — llega en MVP3">
        + ADD LAYER
      </button>
    </section>
  );
}
