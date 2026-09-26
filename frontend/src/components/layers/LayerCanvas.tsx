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
  /**
   * Vista explotada de spec.md M2-S04: cuando es `true`, cada capa se
   * desplaza diagonalmente según su índice EN `layers` (no en
   * `visibleLayers` -- así ocultar una capa no reacomoda a las demás),
   * mediante `transform: translate(dx%, dy%)` sobre un wrapper por capa que
   * envuelve tanto la imagen como su overlay de componentes (se mueven
   * juntos, así los clicks siguen cayendo sobre la pieza que se ve
   * desplazada). El porcentaje es relativo al tamaño PROPIO de cada capa
   * (comportamiento nativo de `transform: translate(%)`), no al del
   * contenedor -- por eso el desplazamiento se ve proporcionalmente igual
   * sin importar el ancho real en pantalla (responsive).
   *
   * Puramente visual: no cambia `sourceWidthPx`/`sourceHeightPx` ni ningún
   * `d` de path, no se envía al backend. Con `exploded=false` no se aplica
   * ningún `style.transform` (en vez de `translate(0%, 0%)`), así se
   * recupera EXACTAMENTE el layout original sin ninguna aproximación.
   */
  exploded?: boolean;
  /** Desplazamiento por índice de capa, en % del tamaño de la capa. 0 = sin separación. Ver `useExplodedView`. */
  separationPercent?: number;
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
 *
 * M2-S04 reutiliza este mismo componente (no uno paralelo) tanto para la
 * vista ensamblada como para la explotada: es la única forma de que la
 * selección de un componente (M2-S03) sea consistente entre ambas vistas
 * sin ningún código adicional -- `selected`/`onSelectComponent` viven en el
 * mismo `useLayerComponents` de LayersPanel, y alternar `exploded` no
 * desmonta este árbol.
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
  exploded = false,
  separationPercent = 0,
}: LayerCanvasProps) {
  const visibleLayers = layers.filter((layer) => visibility[layer.groupId] ?? true);

  const label = exploded
    ? `Vista explotada de ${visibleLayers.length} de ${layers.length} capas visibles, desplazadas ${separationPercent}% por índice para inspección`
    : `Composición combinada de ${visibleLayers.length} de ${layers.length} capas visibles, alineadas sobre la imagen original`;

  return (
    <div
      className={`layer-canvas${exploded ? " layer-canvas--exploded" : ""}`}
      style={{ aspectRatio: `${sourceWidthPx} / ${sourceHeightPx}` }}
      role="group"
      aria-label={label}
    >
      {visibleLayers.length === 0 ? (
        <p className="layer-canvas__empty">Ninguna capa visible. Activá al menos una para verla acá.</p>
      ) : (
        visibleLayers.map((layer) => {
          // Índice en TODAS las capas (no en visibleLayers): ocultar una
          // capa no debe reacomodar el desplazamiento de las demás.
          const layerIndex = layers.indexOf(layer);
          const offsetPercent = exploded ? layerIndex * separationPercent : 0;
          const groupStyle = offsetPercent !== 0 ? { transform: `translate(${offsetPercent}%, ${offsetPercent}%)` } : undefined;
          const components = componentsByGroup?.[layer.groupId];

          return (
            <div key={layer.groupId} className="layer-canvas__layer-group" style={groupStyle}>
              <img
                src={getSvgUrl(layer.vectorId)}
                alt={`Capa ${layer.name}`}
                className="layer-canvas__layer"
                width={sourceWidthPx}
                height={sourceHeightPx}
                loading="lazy"
              />

              {onSelectComponent &&
                components &&
                components.map((component, index) => {
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
                })}
            </div>
          );
        })
      )}
    </div>
  );
}
