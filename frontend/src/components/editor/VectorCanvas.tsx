import { useEffect, useMemo, useRef, useState } from "react";
import type { KeyboardEvent as ReactKeyboardEvent, PointerEvent as ReactPointerEvent } from "react";
import { Group, Layer, Path, Stage } from "react-konva";
import type Konva from "konva";
import type { CanvasTransform } from "../../hooks/useCanvasTransform";
import type { VectorDocumentLayer } from "../../hooks/useVectorDocument";
import { parseLayerSvg, type ParsedLayerSvg } from "../../lib/svgTransform";
import type { EditorTool } from "./EditorToolbar";

const WHEEL_ZOOM_SENSITIVITY = 0.0015;
const KEYBOARD_ZOOM_FACTOR = 1.25;
const KEYBOARD_PAN_STEP_PX = 40;

interface VectorCanvasProps {
  layers: VectorDocumentLayer[];
  visibility: Record<string, boolean>;
  sourceWidthPx: number;
  sourceHeightPx: number;
  selectedGroupId: string | null;
  onSelectGroup: (groupId: string) => void;
  /** Select All in Layer (M2.1-S07): claves `${groupId}:${pathIndex}` de los paths multi-seleccionados -- ver useVectorDocument.selectAllInLayer. Opcional (default: ninguno seleccionado). */
  selectedPathKeys?: ReadonlySet<string>;
  tool: EditorTool;
  transform: CanvasTransform;
  onZoomBy: (factor: number, anchor?: { x: number; y: number }) => void;
  onPanBy: (dx: number, dy: number) => void;
  onMeasure: (size: { width: number; height: number }) => void;
}

type LayerGeometryState = Record<string, { status: "loading" | "ready" | "error"; geometry: ParsedLayerSvg | null }>;

/**
 * Motor gráfico del Workspace (M2.1-S06), Konva/react-konva -- decisión ya
 * tomada y no vuelta a debatir acá (ADR de M2.1-S05,
 * `.sprint/3e8d77b2-6398-81ad-.../IMPL.md`). Reemplaza el `<img>` estático de
 * `components/layers/LayerCanvas.tsx` (M2-S02) por un canvas interactivo:
 * zoom (rueda del mouse, centrado en el puntero) + pan (arrastre) + fit (ver
 * EditorStatusBar) son REALES en esta tarjeta; selección es básica (click
 * selecciona la capa dueña del path, sin multi-select/transform -- esas son
 * herramientas de MVP3, fuera de alcance).
 *
 * Reusa `useCanvasTransform` (M1-S06) para la matemática de zoom/pan en vez
 * de reinventarla: mismo hook, mismo contrato, que ya usa
 * `components/vectorize/VectorCanvas.tsx` para el visualizador de un solo
 * SVG -- acá el "recurso" es la escena Konva completa (todas las capas
 * visibles) en vez de un `<img>`, pero el álgebra de centrar+zoom+pan es
 * idéntica (ver el mismo cálculo de ancla en el handler de `wheel`).
 *
 * El dominio (`VectorLayer`/`VectorDocument`) sigue siendo la fuente de
 * verdad de la geometría: Konva solo la dibuja e interpreta clicks -- nunca
 * "posee" el documento (mismo principio no negociable del ADR).
 */
export function VectorCanvas({
  layers,
  visibility,
  sourceWidthPx,
  sourceHeightPx,
  selectedGroupId,
  onSelectGroup,
  selectedPathKeys,
  tool,
  transform,
  onZoomBy,
  onPanBy,
  onMeasure,
}: VectorCanvasProps) {
  const containerRef = useRef<HTMLDivElement | null>(null);
  const stageRef = useRef<Konva.Stage | null>(null);
  const [containerSize, setContainerSize] = useState({ width: 0, height: 0 });
  const [geometryByVectorId, setGeometryByVectorId] = useState<LayerGeometryState>({});
  const dragStateRef = useRef<{ pointerId: number; lastX: number; lastY: number } | null>(null);
  const pendingPanRef = useRef<{ dx: number; dy: number } | null>(null);
  const rafIdRef = useRef<number | null>(null);
  const [spacePanHeld, setSpacePanHeld] = useState(false);

  const effectiveTool: EditorTool = spacePanHeld ? "pan" : tool;

  // Carga (fetch + parseo) del SVG real de cada capa -- una vez por
  // vectorId, cacheado en estado local; nunca layers hardcodeadas (spec.md,
  // "Integración"). Solo se piden las capas presentes en `layers` (ya
  // filtradas por VectorDocument), independientemente de su visibilidad
  // actual -- así togglear visibilidad no vuelve a pedir el SVG.
  useEffect(() => {
    let cancelled = false;

    for (const layer of layers) {
      if (geometryByVectorId[layer.vectorId]) {
        continue;
      }
      setGeometryByVectorId((current) => ({ ...current, [layer.vectorId]: { status: "loading", geometry: null } }));

      fetch(layer.svgUrl)
        .then((response) => {
          if (!response.ok) throw new Error(`HTTP ${response.status}`);
          return response.text();
        })
        .then((text) => {
          if (cancelled) return;
          const geometry = parseLayerSvg(text, layer.colorHex);
          setGeometryByVectorId((current) => ({ ...current, [layer.vectorId]: { status: "ready", geometry } }));
        })
        .catch(() => {
          if (cancelled) return;
          setGeometryByVectorId((current) => ({ ...current, [layer.vectorId]: { status: "error", geometry: null } }));
        });
    }

    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [layers]);

  useEffect(() => {
    const node = containerRef.current;
    if (!node) return;
    const observer = new ResizeObserver((entries) => {
      const entry = entries[0];
      if (!entry) return;
      const size = { width: entry.contentRect.width, height: entry.contentRect.height };
      setContainerSize(size);
      onMeasure(size);
    });
    observer.observe(node);
    return () => observer.disconnect();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Zoom con la rueda del mouse, centrado en el puntero -- listener nativo
  // (no `onWheel` de JSX) por la misma razón documentada en
  // `components/vectorize/VectorCanvas.tsx`: React adjunta "wheel" como
  // passive por defecto, y acá necesitamos `preventDefault()` para que la
  // rueda no scrollee la página en vez de zoomear el canvas.
  useEffect(() => {
    const node = containerRef.current;
    if (!node) return;

    const handleWheel = (event: WheelEvent) => {
      event.preventDefault();
      const rect = node.getBoundingClientRect();
      const anchor = {
        x: event.clientX - (rect.left + rect.width / 2),
        y: event.clientY - (rect.top + rect.height / 2),
      };
      const factor = Math.exp(-event.deltaY * WHEEL_ZOOM_SENSITIVITY);
      onZoomBy(factor, anchor);
    };

    node.addEventListener("wheel", handleWheel, { passive: false });
    return () => node.removeEventListener("wheel", handleWheel);
  }, [onZoomBy]);

  useEffect(() => {
    return () => {
      if (rafIdRef.current !== null) cancelAnimationFrame(rafIdRef.current);
    };
  }, []);

  const flushPendingPan = () => {
    rafIdRef.current = null;
    const pending = pendingPanRef.current;
    pendingPanRef.current = null;
    if (pending) onPanBy(pending.dx, pending.dy);
  };

  const handlePointerDown = (event: ReactPointerEvent<HTMLDivElement>) => {
    if (event.pointerType === "mouse" && event.button !== 0) return;
    try {
      event.currentTarget.setPointerCapture?.(event.pointerId);
    } catch {
      // noop: continuar sin captura de puntero (ej. entorno de test).
    }
    dragStateRef.current = { pointerId: event.pointerId, lastX: event.clientX, lastY: event.clientY };
  };

  const handlePointerMove = (event: ReactPointerEvent<HTMLDivElement>) => {
    if (effectiveTool !== "pan") return;
    const drag = dragStateRef.current;
    if (!drag || drag.pointerId !== event.pointerId) return;
    const dx = event.clientX - drag.lastX;
    const dy = event.clientY - drag.lastY;
    drag.lastX = event.clientX;
    drag.lastY = event.clientY;

    const pending = pendingPanRef.current ?? { dx: 0, dy: 0 };
    pending.dx += dx;
    pending.dy += dy;
    pendingPanRef.current = pending;

    if (rafIdRef.current === null) {
      rafIdRef.current = requestAnimationFrame(flushPendingPan);
    }
  };

  const endDrag = (event: ReactPointerEvent<HTMLDivElement>) => {
    if (dragStateRef.current?.pointerId !== event.pointerId) return;
    dragStateRef.current = null;
    try {
      if (event.currentTarget.hasPointerCapture?.(event.pointerId)) {
        event.currentTarget.releasePointerCapture?.(event.pointerId);
      }
    } catch {
      // noop.
    }
  };

  const handleKeyDown = (event: ReactKeyboardEvent<HTMLDivElement>) => {
    switch (event.key) {
      case " ":
      case "Spacebar":
        event.preventDefault();
        setSpacePanHeld(true);
        break;
      case "ArrowUp":
        event.preventDefault();
        onPanBy(0, KEYBOARD_PAN_STEP_PX);
        break;
      case "ArrowDown":
        event.preventDefault();
        onPanBy(0, -KEYBOARD_PAN_STEP_PX);
        break;
      case "ArrowLeft":
        event.preventDefault();
        onPanBy(KEYBOARD_PAN_STEP_PX, 0);
        break;
      case "ArrowRight":
        event.preventDefault();
        onPanBy(-KEYBOARD_PAN_STEP_PX, 0);
        break;
      case "+":
      case "=":
        event.preventDefault();
        onZoomBy(KEYBOARD_ZOOM_FACTOR);
        break;
      case "-":
      case "_":
        event.preventDefault();
        onZoomBy(1 / KEYBOARD_ZOOM_FACTOR);
        break;
      default:
        break;
    }
  };

  const handleKeyUp = (event: ReactKeyboardEvent<HTMLDivElement>) => {
    if (event.key === " " || event.key === "Spacebar") {
      setSpacePanHeld(false);
    }
  };

  const visibleLayers = useMemo(() => layers.filter((layer) => visibility[layer.groupId] ?? true), [layers, visibility]);
  const hasContainerSize = containerSize.width > 0 && containerSize.height > 0;

  return (
    <div
      ref={containerRef}
      className={`vector-canvas-2 vector-canvas-2--${effectiveTool}`}
      tabIndex={0}
      role="application"
      aria-label={`Canvas del documento. Herramienta activa: ${effectiveTool === "pan" ? "Pan" : "Select"}. Rueda del mouse para zoom. Mantené Espacio para pan temporal. Con foco: flechas para desplazar, + y - para zoom.`}
      onPointerDown={handlePointerDown}
      onPointerMove={handlePointerMove}
      onPointerUp={endDrag}
      onPointerCancel={endDrag}
      onKeyDown={handleKeyDown}
      onKeyUp={handleKeyUp}
    >
      {layers.length === 0 ? (
        <p className="vector-canvas-2__empty">Ninguna capa visible.</p>
      ) : hasContainerSize ? (
        <Stage
          ref={stageRef}
          width={containerSize.width}
          height={containerSize.height}
          onMouseDown={(event) => {
            // Click en fondo vacío (no sobre ningún <Path>): deselecciona,
            // mismo criterio que el spike (KonvaSpike.tsx).
            if (effectiveTool === "select" && event.target === event.target.getStage()) {
              // No hay "deseleccionar" explícito en VectorDocument (selectedGroupId
              // vive en el padre); no-op intencional -- ver EditorShell.
            }
          }}
        >
          <Layer
            offsetX={sourceWidthPx / 2}
            offsetY={sourceHeightPx / 2}
            x={containerSize.width / 2 + transform.panX}
            y={containerSize.height / 2 + transform.panY}
            scaleX={transform.scale}
            scaleY={transform.scale}
          >
            {visibleLayers.map((layer) => {
              const entry = geometryByVectorId[layer.vectorId];
              if (!entry || entry.status !== "ready" || !entry.geometry) return null;
              const isSelected = selectedGroupId === layer.groupId;

              return (
                // Lock (M2.1-S07, concepto NUEVO): un Layer bloqueado sigue
                // siendo visible/seleccionable/inspeccionable -- `listening`
                // se deja SIEMPRE en su default (true), Select/Select All
                // siguen funcionando igual. `draggable={false}` explícito
                // documenta la intención (hoy MVP3 todavía no agregó ninguna
                // herramienta de transform/drag real sobre paths, así que el
                // comportamiento observable es idéntico al de una capa
                // desbloqueada -- ver IMPL.md, "Lock en el Canvas").
                <Group key={layer.groupId} name={`vector-canvas-layer-group ${layer.locked ? "vector-canvas-layer-group--locked" : ""}`} draggable={false}>
                  {entry.geometry.paths.map((path, index) => {
                    const pathKey = `${layer.groupId}:${index}`;
                    const isPathMultiSelected = selectedPathKeys?.has(pathKey) ?? false;
                    const stroke = isPathMultiSelected ? "#f5a623" : isSelected ? "#3a5cf5" : undefined;
                    const strokeWidth = isPathMultiSelected || isSelected ? Math.max(1, 2 / transform.scale) : 0;

                    return (
                      <Path
                        key={index}
                        data={path.d}
                        fill={path.fill}
                        x={path.transform.x}
                        y={path.transform.y}
                        rotation={path.transform.rotation}
                        scaleX={path.transform.scaleX}
                        scaleY={path.transform.scaleY}
                        skewX={path.transform.skewX}
                        stroke={stroke}
                        strokeWidth={strokeWidth}
                        draggable={false}
                        onClick={() => {
                          if (effectiveTool === "select") onSelectGroup(layer.groupId);
                        }}
                        onTap={() => {
                          if (effectiveTool === "select") onSelectGroup(layer.groupId);
                        }}
                      />
                    );
                  })}
                </Group>
              );
            })}
          </Layer>
        </Stage>
      ) : null}
    </div>
  );
}
