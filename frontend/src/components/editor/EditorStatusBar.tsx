const ZOOM_BUTTON_FACTOR = 1.25;

interface EditorStatusBarProps {
  scale: number;
  onZoomBy: (factor: number) => void;
  onFit: () => void;
  sourceWidthPx: number;
  sourceHeightPx: number;
}

/**
 * Barra inferior del wireframe obligatorio: "− 68% + │ FIT │ ... │ 210 ×
 * 297 mm │ GRID │ SNAP". `PaletteBar` (swatches) se renderiza aparte en el
 * mismo `<footer>` de `EditorShell` -- acá van zoom/fit/dimensiones/grid-snap.
 *
 * Dimensiones: se muestran en PX (tamaño interno real del SVG, siempre
 * disponible desde `VectorDocument`) en vez de mm -- mostrar "210 × 297 mm"
 * sin que el proyecto tenga una `DimensionVersion` aplicada (M1-S09) sería
 * un dato inventado (spec.md, DoD: "no hay datos falsos"). Si más adelante
 * el Workspace conoce una `DimensionVersion` vigente, este componente puede
 * extenderse para mostrarla -- documentado como decisión en IMPL.md, no
 * implementado en esta tarjeta (fuera del alcance explícito del spec, que
 * solo pide mm "si ya hay una versión con dimensión aplicada").
 */
export function EditorStatusBar({ scale, onZoomBy, onFit, sourceWidthPx, sourceHeightPx }: EditorStatusBarProps) {
  const zoomPercent = Math.round(scale * 100);

  return (
    <div className="editor-status-bar" role="group" aria-label="Controles de zoom y documento">
      <div className="editor-status-bar__zoom">
        <button
          type="button"
          className="editor-status-bar__button"
          aria-label="Alejar"
          title="Alejar (-)"
          onClick={() => onZoomBy(1 / ZOOM_BUTTON_FACTOR)}
        >
          −
        </button>
        <span className="editor-status-bar__zoom-value" aria-label={`Zoom actual: ${zoomPercent}%`}>
          {zoomPercent}%
        </span>
        <button
          type="button"
          className="editor-status-bar__button"
          aria-label="Acercar"
          title="Acercar (+)"
          onClick={() => onZoomBy(ZOOM_BUTTON_FACTOR)}
        >
          +
        </button>
      </div>

      <button type="button" className="editor-status-bar__button editor-status-bar__fit" onClick={onFit} title="Ajustar el documento completo a la pantalla">
        FIT
      </button>

      <span className="editor-status-bar__dimensions" aria-label={`Tamaño del documento: ${sourceWidthPx} por ${sourceHeightPx} píxeles`}>
        {sourceWidthPx} × {sourceHeightPx} px
      </span>

      <button
        type="button"
        className="editor-status-bar__button editor-status-bar__placeholder"
        disabled
        aria-label="Grilla (llega en MVP3)"
        title="Grilla — llega en MVP3"
      >
        GRID
      </button>
      <button
        type="button"
        className="editor-status-bar__button editor-status-bar__placeholder"
        disabled
        aria-label="Ajuste a grilla (llega en MVP3)"
        title="Snap — llega en MVP3"
      >
        SNAP
      </button>
    </div>
  );
}
