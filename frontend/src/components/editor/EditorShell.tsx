import { useState } from "react";
import { useCanvasTransform } from "../../hooks/useCanvasTransform";
import { useVectorDocument } from "../../hooks/useVectorDocument";
import { EditorHeader } from "./EditorHeader";
import { EditorLayersPanel } from "./EditorLayersPanel";
import { EditorStatusBar } from "./EditorStatusBar";
import { EditorToolbar, type EditorTool } from "./EditorToolbar";
import { InspectorPanel } from "./InspectorPanel";
import { PaletteBar } from "./PaletteBar";
import { PreviewNavigator } from "./PreviewNavigator";
import { VectorCanvas } from "./VectorCanvas";
import "./editor.css";

export interface EditorShellProps {
  projectId: string;
  imageId: string;
  /** Sesión de paleta YA CONFIRMADA -- precondición para abrir el Workspace (ver useVectorDocument). */
  paletteId: string;
  projectName: string;
  onClose: () => void;
}

const EMPTY_REASON_COPY: Record<string, string> = {
  no_palette_selected: "Este proyecto todavía no tiene una paleta de colores confirmada.",
  palette_not_found: "No se encontró la sesión de paleta de colores de este proyecto.",
  palette_not_confirmed: "La paleta de colores de este proyecto todavía no fue confirmada.",
  layers_not_generated:
    "Este proyecto todavía no tiene capas vectoriales generadas. Generalas desde el panel de Capas por color antes de abrir el Workspace.",
};

/**
 * Layout raíz del Workspace (M2.1-S06): integra en una sola pantalla Canvas,
 * Toolbar, Layers, Palette, Preview, Inspector y controles de documento --
 * ver spec.md, wireframe obligatorio. El estado de dominio vive en
 * `useVectorDocument` (agregación cliente de paleta confirmada + capas +
 * consolidado, ver ese hook); el estado de VISTA (zoom/pan, herramienta
 * activa, tamaño medido del canvas) vive acá, compartido entre VectorCanvas/
 * PreviewNavigator/EditorStatusBar -- ningún panel visual guarda su propia
 * copia divergente de ninguno de los dos.
 */
export function EditorShell({ projectId, imageId, paletteId, projectName, onClose }: EditorShellProps) {
  const {
    status,
    document,
    emptyReason,
    errorMessage,
    reload,
    visibility,
    toggleVisibility,
    isolate,
    showAll,
    selectedGroupId,
    selectGroup,
  } = useVectorDocument(projectId, imageId, paletteId);

  const { transform, zoomBy, panBy, fitToScreen } = useCanvasTransform();
  const [activeTool, setActiveTool] = useState<EditorTool>("select");
  const [canvasSize, setCanvasSize] = useState({ width: 0, height: 0 });

  const selectedLayer = document?.layers.find((layer) => layer.groupId === selectedGroupId) ?? null;
  const isSelectedVisible = selectedGroupId ? (visibility[selectedGroupId] ?? true) : false;

  const handleFit = () => {
    if (!document) return;
    fitToScreen(canvasSize, { width: document.sourceWidthPx, height: document.sourceHeightPx });
  };

  return (
    <div className="editor-shell">
      <EditorHeader projectName={projectName} onClose={onClose} />

      <div className="editor-shell__body">
        <EditorToolbar activeTool={activeTool} onSelectTool={setActiveTool} />

        <main className="editor-shell__canvas-area" aria-label="Canvas del documento">
          {status === "loading" || status === "idle" ? (
            <p className="editor-shell__status" role="status">
              Cargando el documento del proyecto…
            </p>
          ) : status === "error" ? (
            <div className="editor-shell__status editor-shell__status--error" role="alert">
              <p>{errorMessage ?? "No se pudo cargar el documento del proyecto."}</p>
              <button type="button" className="upload-actions__button" onClick={reload}>
                Reintentar
              </button>
            </div>
          ) : status === "empty" ? (
            <p className="editor-shell__status" role="status">
              {EMPTY_REASON_COPY[emptyReason ?? ""] ?? "No hay contenido para mostrar todavía."}
            </p>
          ) : document ? (
            <VectorCanvas
              layers={document.layers}
              visibility={visibility}
              sourceWidthPx={document.sourceWidthPx}
              sourceHeightPx={document.sourceHeightPx}
              selectedGroupId={selectedGroupId}
              onSelectGroup={selectGroup}
              tool={activeTool}
              transform={transform}
              onZoomBy={zoomBy}
              onPanBy={panBy}
              onMeasure={setCanvasSize}
            />
          ) : null}
        </main>

        <aside className="editor-shell__right-rail" aria-label="Paneles del documento">
          <PreviewNavigator
            layers={document?.layers ?? []}
            visibility={visibility}
            sourceWidthPx={document?.sourceWidthPx ?? 0}
            sourceHeightPx={document?.sourceHeightPx ?? 0}
            transform={transform}
            viewportSize={canvasSize}
          />

          <EditorLayersPanel
            layers={document?.layers ?? []}
            visibility={visibility}
            onToggleVisibility={toggleVisibility}
            selectedGroupId={selectedGroupId}
            onSelectGroup={selectGroup}
          />

          <InspectorPanel
            layers={document?.layers ?? []}
            selectedLayer={selectedLayer}
            isVisible={isSelectedVisible}
            onIsolate={() => {
              if (selectedGroupId) isolate(selectedGroupId);
            }}
            onShowAll={showAll}
            onRefresh={reload}
          />
        </aside>
      </div>

      <footer className="editor-shell__bottombar">
        <EditorStatusBar
          scale={transform.scale}
          onZoomBy={zoomBy}
          onFit={handleFit}
          sourceWidthPx={document?.sourceWidthPx ?? 0}
          sourceHeightPx={document?.sourceHeightPx ?? 0}
        />
        <PaletteBar layers={document?.layers ?? []} selectedGroupId={selectedGroupId} onSelectGroup={selectGroup} />
      </footer>
    </div>
  );
}
