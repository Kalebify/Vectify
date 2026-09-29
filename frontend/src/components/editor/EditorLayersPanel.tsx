import type { VectorDocumentLayer } from "../../hooks/useVectorDocument";
import { OPERATION_LABEL } from "../layers/manufacturingOperationLabels";

interface EditorLayersPanelProps {
  layers: VectorDocumentLayer[];
  visibility: Record<string, boolean>;
  onToggleVisibility: (groupId: string) => void;
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
 * "+ ADD LAYER" del wireframe queda deshabilitado: crear una capa nueva
 * desde cero (no derivada de un color detectado) es una herramienta de
 * edición real, fuera de alcance de esta tarjeta (spec.md, "Fuera de
 * alcance").
 */
export function EditorLayersPanel({ layers, visibility, onToggleVisibility, selectedGroupId, onSelectGroup }: EditorLayersPanelProps) {
  return (
    <section aria-labelledby="editor-layers-heading" className="editor-layers-panel">
      <h3 id="editor-layers-heading" className="editor-panel__heading">
        Layers
      </h3>

      {layers.length === 0 ? (
        <p className="editor-panel__empty">Este proyecto todavía no tiene capas generadas.</p>
      ) : (
        <ul className="editor-layers-panel__list" aria-label="Capas del documento">
          {layers.map((layer) => {
            const isVisible = visibility[layer.groupId] ?? true;
            const isSelected = selectedGroupId === layer.groupId;

            return (
              <li
                key={layer.groupId}
                className={`editor-layers-panel__row${isSelected ? " editor-layers-panel__row--selected" : ""}`}
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

                <span className="editor-layers-panel__lock" aria-hidden="true" title="Bloqueo de capa (llega en MVP3)">
                  🔒
                </span>
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
