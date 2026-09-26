import type { VectorLayerPayload } from "../../types/vectorLayers";

interface LayerListProps {
  layers: VectorLayerPayload[];
  visibility: Record<string, boolean>;
  onToggleVisibility: (groupId: string) => void;
}

/**
 * Lista de capas de spec.md M2-S02 ("React"): swatch de color, nombre
 * (heredado del grupo de color, solo lectura -- renombrar es una operación
 * de M2-S01 sobre la paleta, no de esta tarjeta), porcentaje de área
 * aproximada y toggle de visibilidad individual. Puramente presentacional.
 */
export function LayerList({ layers, visibility, onToggleVisibility }: LayerListProps) {
  return (
    <ul className="layer-list" aria-label="Capas vectoriales por color">
      {layers.map((layer) => {
        const isVisible = visibility[layer.groupId] ?? true;
        return (
          <li key={layer.groupId} className="layer-row" aria-label={`Capa ${layer.name}`}>
            <span
              className="layer-row__color"
              style={{ backgroundColor: layer.colorHex }}
              aria-hidden="true"
              title={layer.colorHex}
            />
            <span className="layer-row__name">{layer.name}</span>
            <span className="layer-row__area">{layer.areaPercent.toFixed(1)}%</span>

            {layer.hasPartialAlpha && (
              <span className="color-swatch__badge" title="Parte de este grupo tiene transparencia parcial">
                semi-transparente
              </span>
            )}

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
