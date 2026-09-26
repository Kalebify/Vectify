import type { LayerComponentPayload } from "../../types/components";
import type { SelectedComponent } from "../../hooks/useLayerComponents";
import type { VectorLayerPayload } from "../../types/vectorLayers";

interface ComponentTreeProps {
  layers: VectorLayerPayload[];
  componentsByGroup: Record<string, LayerComponentPayload[]>;
  selected: SelectedComponent | null;
  onSelect: (groupId: string, componentId: string) => void;
}

function pieceCountLabel(count: number): string {
  return `${count} ${count === 1 ? "pieza" : "piezas"}`;
}

/**
 * Árbol "Layer -> Components" de spec.md M2-S03: cada layer expande a sus
 * componentes físicos (ej. "Azul: 3 piezas"), con selección -- cada
 * componente es un `<button>` (no un `<div>` con onClick) que notifica al
 * padre para resaltarlo en LayerCanvas y viceversa (ver ComponentOverlay).
 * Solo muestra las capas cuyos componentes YA se calcularon (ver
 * useLayerComponents.compute) -- las que todavía no, simplemente no
 * aparecen en el árbol todavía.
 */
export function ComponentTree({ layers, componentsByGroup, selected, onSelect }: ComponentTreeProps) {
  const layersWithComponents = layers.filter((layer) => componentsByGroup[layer.groupId] !== undefined);

  if (layersWithComponents.length === 0) {
    return null;
  }

  return (
    <ul className="component-tree" aria-label="Árbol de capas y sus componentes físicos">
      {layersWithComponents.map((layer) => {
        const components = componentsByGroup[layer.groupId]!;
        return (
          <li key={layer.groupId} className="component-tree__layer">
            <div className="component-tree__layer-header">
              <span className="layer-row__color" style={{ backgroundColor: layer.colorHex }} aria-hidden="true" />
              <span className="component-tree__layer-name">
                {layer.name}: {pieceCountLabel(components.length)}
              </span>
            </div>

            <ul className="component-tree__components" aria-label={`Componentes de la capa ${layer.name}`}>
              {components.map((component, index) => {
                const isSelected = selected?.groupId === layer.groupId && selected?.componentId === component.id;
                const label = `Pieza ${index + 1} de ${layer.name}${component.isTiny ? " (diminuta)" : ""}`;
                return (
                  <li key={component.id}>
                    <button
                      type="button"
                      className="component-tree__item"
                      aria-pressed={isSelected}
                      aria-label={label}
                      onClick={() => onSelect(layer.groupId, component.id)}
                    >
                      <span className="component-tree__item-name">Pieza {index + 1}</span>
                      {component.isTiny && <span className="component-tree__tiny-badge">diminuta</span>}
                    </button>
                  </li>
                );
              })}
            </ul>
          </li>
        );
      })}
    </ul>
  );
}
