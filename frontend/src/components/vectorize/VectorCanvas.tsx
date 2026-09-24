import { useEffect, useRef } from "react";
import type { CSSProperties, KeyboardEvent, PointerEvent } from "react";
import type { CanvasTransform } from "../../hooks/useCanvasTransform";

const WHEEL_ZOOM_SENSITIVITY = 0.0015;
const KEYBOARD_ZOOM_FACTOR = 1.25;
const KEYBOARD_PAN_STEP_PX = 40;

interface VectorCanvasProps {
  /** Nombre accesible del panel (ej. "Original", "SVG vectorizado"), usado en aria-label. */
  label: string;
  src: string;
  alt: string;
  /** Tamaño natural del recurso en px. Si falta, el zoom/pan sigue funcionando pero fitToScreen no puede calcularse (ver useCanvasTransform.fitToScreen). */
  intrinsicWidth: number | null;
  intrinsicHeight: number | null;
  transform: CanvasTransform;
  onZoomBy: (factor: number, anchor?: { x: number; y: number }) => void;
  onPanBy: (dx: number, dy: number) => void;
  /** Reporta el tamaño del contenedor (para fit-to-screen) cada vez que cambia, incluida la primera medición. */
  onMeasure?: (size: { width: number; height: number }) => void;
}

/**
 * Panel individual de zoom/pan de spec.md M1-S06 ("Usuario podrá: Ver SVG,
 * zoom, pan, fit-to-screen"). Puramente presentacional + manejo de input: la
 * escala/desplazamiento vive en el padre (useCanvasTransform), compartida
 * entre el panel "original" y el panel "vector" de VectorComparison para
 * garantizar la misma escala de referencia entre ambos (Definition of Done).
 *
 * El zoom/pan NUNCA reescribe el `src` ni el contenido del recurso: solo
 * aplica `transform: translate(...) scale(...)` sobre el <img> que lo
 * contiene. Se mantiene <img> (no <svg> inline + dangerouslySetInnerHTML)
 * por la misma razón documentada en VectorizePanel.tsx (M1-S05): defensa en
 * profundidad, el navegador nunca ejecuta script embebido dentro de un
 * <img> aunque el SVG ya esté saneado en el backend. El transform CSS sobre
 * un <img> alcanza para zoom/pan/reset/fit-to-screen sin necesitar acceso al
 * DOM interno del SVG (que está fuera de alcance: no hay selección de nodos
 * ni edición en este sprint).
 */
export function VectorCanvas({
  label,
  src,
  alt,
  intrinsicWidth,
  intrinsicHeight,
  transform,
  onZoomBy,
  onPanBy,
  onMeasure,
}: VectorCanvasProps) {
  const containerRef = useRef<HTMLDivElement | null>(null);
  const dragStateRef = useRef<{ pointerId: number; lastX: number; lastY: number } | null>(null);
  const pendingPanRef = useRef<{ dx: number; dy: number } | null>(null);
  const rafIdRef = useRef<number | null>(null);

  // Listener de "wheel" adjuntado a mano (no vía onWheel de React): desde
  // React 17 el listener delegado de "wheel" se registra como passive por
  // defecto para no bloquear el scroll de la página, y un preventDefault()
  // dentro de un onWheel de JSX no tiene efecto de forma confiable en ese
  // caso. Acá SÍ necesitamos preventDefault -- si no, la rueda scrollea la
  // página entera en vez de hacer zoom sobre el canvas.
  useEffect(() => {
    const node = containerRef.current;
    if (!node) {
      return;
    }

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

  // Mide el contenedor (para fit-to-screen) y se limpia al desmontar. Un
  // ResizeObserver que no se desconecta es un leak clásico -- acá se
  // desconecta explícitamente en el cleanup del efecto.
  useEffect(() => {
    const node = containerRef.current;
    if (!node || !onMeasure) {
      return;
    }

    const observer = new ResizeObserver((entries) => {
      const entry = entries[0];
      if (!entry) {
        return;
      }
      onMeasure({ width: entry.contentRect.width, height: entry.contentRect.height });
    });
    observer.observe(node);
    return () => observer.disconnect();
  }, [onMeasure]);

  // Cancela cualquier rAF de pan pendiente al desmontar (evita actualizar
  // estado de un componente ya desmontado si el usuario suelta el drag justo
  // cuando la sección deja de renderizarse).
  useEffect(() => {
    return () => {
      if (rafIdRef.current !== null) {
        cancelAnimationFrame(rafIdRef.current);
      }
    };
  }, []);

  const flushPendingPan = () => {
    rafIdRef.current = null;
    const pending = pendingPanRef.current;
    pendingPanRef.current = null;
    if (pending) {
      onPanBy(pending.dx, pending.dy);
    }
  };

  // Pan por arrastre vía Pointer Events + pointer capture: los handlers
  // quedan sobre el propio elemento (no en document/window), así que no hay
  // listeners globales que limpiar manualmente ni riesgo de que sigan
  // reaccionando a moves fuera del contenedor tras desmontar.
  const handlePointerDown = (event: PointerEvent<HTMLDivElement>) => {
    if (event.pointerType === "mouse" && event.button !== 0) {
      return;
    }
    // setPointerCapture puede faltar (entornos de test, navegadores muy
    // viejos) o lanzar (pointerId ya inválido si el usuario soltó y volvió a
    // presionar muy rápido). El drag igual funciona sin captura explícita
    // -- solo se pierde la garantía de seguir recibiendo pointermove fuera
    // del elemento -- así que degradamos en vez de romper el handler.
    try {
      event.currentTarget.setPointerCapture?.(event.pointerId);
    } catch {
      // noop: continuar sin captura de puntero.
    }
    dragStateRef.current = { pointerId: event.pointerId, lastX: event.clientX, lastY: event.clientY };
  };

  const handlePointerMove = (event: PointerEvent<HTMLDivElement>) => {
    const drag = dragStateRef.current;
    if (!drag || drag.pointerId !== event.pointerId) {
      return;
    }
    const dx = event.clientX - drag.lastX;
    const dy = event.clientY - drag.lastY;
    drag.lastX = event.clientX;
    drag.lastY = event.clientY;

    const pending = pendingPanRef.current ?? { dx: 0, dy: 0 };
    pending.dx += dx;
    pending.dy += dy;
    pendingPanRef.current = pending;

    // Throttle vía rAF: con diseños grandes (ver VectorizePanel, criterio de
    // "diseño grande"), aplicar cada evento pointermove sin batching puede
    // disparar más repintados del SVG de los que el navegador puede seguir.
    if (rafIdRef.current === null) {
      rafIdRef.current = requestAnimationFrame(flushPendingPan);
    }
  };

  const endDrag = (event: PointerEvent<HTMLDivElement>) => {
    if (dragStateRef.current?.pointerId !== event.pointerId) {
      return;
    }
    dragStateRef.current = null;
    try {
      if (event.currentTarget.hasPointerCapture?.(event.pointerId)) {
        event.currentTarget.releasePointerCapture?.(event.pointerId);
      }
    } catch {
      // noop: nada que liberar si nunca se pudo capturar.
    }
  };

  // Controles de teclado (foco visible ya lo da :focus-visible global) para
  // quien no tiene mouse: flechas para desplazar, +/- para zoom. Ver spec.md
  // M1-S06, "Umbrales de calidad": atención razonable a accesibilidad sin
  // auditoría formal.
  const handleKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    switch (event.key) {
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

  // translate(pan) se aplica DESPUÉS de scale() en la lista de funciones (es
  // decir, actúa sobre el punto antes de que scale lo escale -- ver
  // useCanvasTransform.ts): panX/panY quedan en px de pantalla sin importar
  // el zoom vigente, que es justo lo que produce onPanBy (deltas de puntero
  // en px de pantalla).
  const imageStyle: CSSProperties = {
    transform: `translate(-50%, -50%) translate(${transform.panX}px, ${transform.panY}px) scale(${transform.scale})`,
  };

  return (
    <div
      ref={containerRef}
      className="vector-canvas"
      tabIndex={0}
      aria-label={`${label}. Rueda del mouse o pellizco para zoom, arrastrar para desplazar. Con foco: flechas para desplazar, + y - para zoom.`}
      onPointerDown={handlePointerDown}
      onPointerMove={handlePointerMove}
      onPointerUp={endDrag}
      onPointerCancel={endDrag}
      onKeyDown={handleKeyDown}
    >
      <img
        src={src}
        alt={alt}
        className="vector-canvas__image"
        style={imageStyle}
        width={intrinsicWidth ?? undefined}
        height={intrinsicHeight ?? undefined}
        draggable={false}
      />
    </div>
  );
}
