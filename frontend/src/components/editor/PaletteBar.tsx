import type { VectorDocumentLayer } from "../../hooks/useVectorDocument";

interface PaletteBarProps {
  layers: VectorDocumentLayer[];
  selectedGroupId: string | null;
  onSelectGroup: (groupId: string) => void;
}

/**
 * Barra de swatches de la paleta CONFIRMADA, en la barra inferior del
 * wireframe obligatorio ("🔵 🟡 🔴 ⚫ [+]") -- explícitamente DISTINTA de
 * `ColorPalettePanel` (detección/edición de paleta, M2-S01/M2.1-S02): acá
 * solo se puede seleccionar un color (mismo `groupId` COMPARTIDO que el
 * resto del Workspace -- VectorCanvas/EditorLayersPanel/InspectorPanel),
 * nunca fusionar/renombrar/excluir. El "[+]" del wireframe (agregar un color
 * nuevo a la paleta) queda deshabilitado: eso es una operación de
 * detección/edición de paleta, no de este panel (spec.md, "Fuera de
 * alcance" de esta tarjeta -- ni Fill ni Color son reales acá todavía).
 */
export function PaletteBar({ layers, selectedGroupId, onSelectGroup }: PaletteBarProps) {
  return (
    <div className="palette-bar" role="group" aria-label="Paleta confirmada del documento">
      {layers.length === 0 ? (
        <span className="palette-bar__empty">Sin paleta confirmada</span>
      ) : (
        <ul className="palette-bar__list">
          {layers.map((layer) => {
            const isSelected = selectedGroupId === layer.groupId;
            return (
              <li key={layer.groupId}>
                <button
                  type="button"
                  className={`palette-bar__swatch${isSelected ? " palette-bar__swatch--selected" : ""}`}
                  style={{ backgroundColor: layer.colorHex }}
                  aria-pressed={isSelected}
                  aria-label={`Seleccionar el color ${layer.name} (${layer.colorHex})`}
                  title={`${layer.name} — ${layer.colorHex}`}
                  onClick={() => onSelectGroup(layer.groupId)}
                >
                  {isSelected && (
                    <span className="palette-bar__swatch-check" aria-hidden="true">
                      ✓
                    </span>
                  )}
                </button>
              </li>
            );
          })}
        </ul>
      )}

      <button
        type="button"
        className="palette-bar__add"
        disabled
        title="Agregar color a la paleta — llega en MVP3"
        aria-label="Agregar color a la paleta (llega en MVP3)"
      >
        +
      </button>
    </div>
  );
}
