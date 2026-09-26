import type { LayerComponentPayload } from "../../types/components";
import type { VectorLayerPayload } from "../../types/vectorLayers";

interface ExplodedLegendProps {
  layers: VectorLayerPayload[];
  componentsByGroup: Record<string, LayerComponentPayload[]>;
}

function pieceCountLabel(count: number): string {
  return `${count} ${count === 1 ? "pieza" : "piezas"}`;
}

/**
 * Leyenda de spec.md M2-S04: "lista de colores/capas visible en la vista
 * explotada, con nombre y color de cada una" + "contador de piezas por
 * color". El contador reutiliza el dato YA CALCULADO por useLayerComponents
 * (M2-S03) -- no recalcula nada acá: si una capa todavía no tiene
 * componentes calculados (el usuario no pulsó "Calcular componentes" en
 * ComponentTree), esa fila solo muestra nombre+color, sin inventar una
 * cifra.
 */
export function ExplodedLegend({ layers, componentsByGroup }: ExplodedLegendProps) {
  return (
    <ul className="exploded-legend" aria-label="Leyenda de colores y piezas de la vista explotada">
      {layers.map((layer) => {
        const components = componentsByGroup[layer.groupId];
        return (
          <li key={layer.groupId} className="exploded-legend__item" aria-label={`Leyenda de la capa ${layer.name}`}>
            <span className="layer-row__color" style={{ backgroundColor: layer.colorHex }} aria-hidden="true" />
            <span className="exploded-legend__name">{layer.name}</span>
            {components && <span className="exploded-legend__count">{pieceCountLabel(components.length)}</span>}
          </li>
        );
      })}
    </ul>
  );
}
