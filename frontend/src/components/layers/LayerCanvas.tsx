import type { LayerComponentPayload } from "../../types/components";
import type { SelectedComponent } from "../../hooks/useLayerComponents";
import type { VectorLayerPayload } from "../../types/vectorLayers";

interface LayerCanvasProps {
  layers: VectorLayerPayload[];
  visibility: Record<string, boolean>;
  sourceWidthPx: number;
  sourceHeightPx: number;
  getSvgUrl: (vectorId: string) => string;
  /**
   * Componentes físicos por capa (M2-S03), opcional -- si se provee (junto
   * con `onSelectComponent`), se dibuja un overlay clickeable por
   * componente sobre cada capa VISIBLE, usando sus `bounds` (mismas
   * unidades que sourceWidthPx/sourceHeightPx, ver spec.md M2-S02:
   * "normalización de coordenadas") para posicionarlo con inset en
   * porcentaje -- selección bidireccional lista/canvas.
   */
  componentsByGroup?: Record<string, LayerComponentPayload[]>;
  selected?: SelectedComponent | null;
  onSelectComponent?: (groupId: string, componentId: string) => void;
}

/**
 * Canvas combinado de spec.md M2-S02 ("React"): superpone los SVG de las
 * capas VISIBLES, alineados correctamente sobre la imagen base. La
 * alineación es automática y no requiere ningún cálculo de posición propio
 * de este componente: cada capa comparte el mismo sistema de coordenadas/
 * viewBox que la imagen original (Vectify.Api/el motor Python nunca recorta
 * una máscara a su propio bounding box antes de vectorizarla -- ver
 * spec.md, "normalización de coordenadas"), así que apilar cada `<img>` con
 * el mismo width/height en la misma posición (position: absolute; inset: 0)
 * ya las deja encajadas exactamente unas sobre otras. Mismo criterio de
 * "defensa en profundidad" que VectorCanvas.tsx (M1-S06): se mantienen
 * `<img>` (no `<svg>` inline + dangerouslySetInnerHTML) -- el navegador
 * nunca ejecuta script embebido dentro de un `<img>` aunque el SVG ya esté
 * saneado en el backend.
 */
export function LayerCanvas({
  layers,
  visibility,
  sourceWidthPx,
  sourceHeightPx,
  getSvgUrl,
  componentsByGroup,
  selected,
  onSelectComponent,
}: LayerCanvasProps) {
  const visibleLayers = layers.filter((layer) => visibility[layer.groupId] ?? true);

  return (
    <div
      className="layer-canvas"
      style={{ aspectRatio: `${sourceWidthPx} / ${sourceHeightPx}` }}
      role="group"
      aria-label={`Composición combinada de ${visibleLayers.length} de ${layers.length} capas visibles, alineadas sobre la imagen original`}
    >
      {visibleLayers.length === 0 ? (
        <p className="layer-canvas__empty">Ninguna capa visible. Activá al menos una para verla acá.</p>
      ) : (
        visibleLayers.map((layer) => (
          <img
            key={layer.groupId}
            src={getSvgUrl(layer.vectorId)}
            alt={`Capa ${layer.name}`}
            className="layer-canvas__layer"
            width={sourceWidthPx}
            height={sourceHeightPx}
            loading="lazy"
          />
        ))
      )}

      {onSelectComponent &&
        visibleLayers.map((layer) => {
          const components = componentsByGroup?.[layer.groupId];
          if (!components) {
            return null;
          }

          return components.map((component, index) => {
            const isSelected = selected?.groupId === layer.groupId && selected?.componentId === component.id;
            const widthPercent = ((component.bounds.maxX - component.bounds.minX) / sourceWidthPx) * 100;
            const heightPercent = ((component.bounds.maxY - component.bounds.minY) / sourceHeightPx) * 100;

            return (
              <button
                key={`${layer.groupId}-${component.id}`}
                type="button"
                className={`layer-canvas__component${isSelected ? " layer-canvas__component--selected" : ""}`}
                style={{
                  left: `${(component.bounds.minX / sourceWidthPx) * 100}%`,
                  top: `${(component.bounds.minY / sourceHeightPx) * 100}%`,
                  width: `${widthPercent}%`,
                  height: `${heightPercent}%`,
                }}
                aria-pressed={isSelected}
                aria-label={`Pieza ${index + 1} de ${layer.name}`}
                onClick={() => onSelectComponent(layer.groupId, component.id)}
              />
            );
          });
        })}
    </div>
  );
}
