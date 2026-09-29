import type { SpikeLayer } from "../types";

/**
 * Panel de layers COMPARTIDO por los 3 candidatos: React es dueño del
 * estado (visible/isolated/selected viven en el componente padre de cada
 * candidato, no dentro del motor gráfico) y el motor gráfico solo
 * recibe callbacks. Esto demuestra en código la restricción arquitectónica
 * no negociable del spec: "React controla layout/toolbar/inspector/layers
 * y estado de aplicación. El motor gráfico controla Canvas y geometría
 * interactiva." — el panel es intencionalmente idéntico entre los 3 tabs;
 * lo único que cambia es cómo cada motor reacciona a los callbacks.
 */
export function LayersPanel(props: {
  layers: SpikeLayer[];
  selectedIds: Set<string>;
  hiddenIds: Set<string>;
  isolatedId: string | null;
  onSelect: (id: string, additive: boolean) => void;
  onToggleVisible: (id: string) => void;
  onIsolate: (id: string | null) => void;
}) {
  const { layers, selectedIds, hiddenIds, isolatedId, onSelect, onToggleVisible, onIsolate } = props;
  return (
    <ul className="layers-panel">
      {layers.map((layer) => {
        const isHidden = hiddenIds.has(layer.id);
        const isSelected = selectedIds.has(layer.id);
        const isIsolated = isolatedId === layer.id;
        return (
          <li
            key={layer.id}
            className={`layers-panel__row${isSelected ? " is-selected" : ""}`}
          >
            <input
              type="checkbox"
              checked={!isHidden}
              onChange={() => onToggleVisible(layer.id)}
              aria-label={`Mostrar/ocultar ${layer.name}`}
            />
            <span
              className="layers-panel__swatch"
              style={{ background: layer.colorHex }}
              aria-hidden="true"
            />
            <button
              type="button"
              className="layers-panel__name"
              onClick={(e) => onSelect(layer.id, e.shiftKey || e.metaKey || e.ctrlKey)}
            >
              {layer.name}
            </button>
            <button
              type="button"
              className={`layers-panel__isolate${isIsolated ? " is-active" : ""}`}
              onClick={() => onIsolate(isIsolated ? null : layer.id)}
              title="Isolate layer"
            >
              iso
            </button>
          </li>
        );
      })}
    </ul>
  );
}
