import type { WorkspaceSaveState } from "../../hooks/useWorkspaceSave";

interface EditorHeaderProps {
  projectName: string;
  onClose: () => void;
  /** Estado real de guardado (M2.2-S05) -- ver useWorkspaceSave. */
  saveState: WorkspaceSaveState;
  saveErrorMessage: string | null;
  /** Deshabilita el botón Guardar mientras el documento todavía no terminó de cargar (sin paleta/versión resueltas). */
  canSave: boolean;
  onSave: () => void;
}

const DIRTY_STATE_COPY: Record<WorkspaceSaveState, string> = {
  idle: "Sin cambios desde la última apertura",
  dirty: "Cambios sin guardar",
  saving: "Guardando…",
  saved: "Guardado",
  error: "No se pudo guardar",
};

/**
 * Header del wireframe obligatorio: "VECTORiZE ← Projects Project.svg ✓ ↶
 * ↷ SAVE EXPORT".
 *
 * Guardado real (M2.2-S05): reemplaza el placeholder estático de versiones
 * anteriores ("Sin guardado automático todavía", botón SAVE siempre
 * deshabilitado) por la máquina de estados real `useWorkspaceSave`
 * (idle/dirty/saving/saved/error) -- ver ese hook para el detalle completo.
 * El Save sigue siendo una acción EXPLÍCITA del usuario (sin autosave/
 * debounce, fuera de alcance de esta tarjeta): el botón solo dispara
 * `onSave`, nunca se llama solo.
 *
 * Decisiones documentadas en IMPL.md (spec.md, "Ambigüedades detectadas"):
 * - **Undo/Redo**: placeholders deshabilitados, sin funcionalidad (esta
 *   tarjeta no implementa historial de edición).
 * - **Export**: placeholder deshabilitado. `ExportPanel` (M1-S10) exporta
 *   UN `VectorVersion`/`SimplificationVersion`/`DimensionVersion` del
 *   pipeline de un solo vector de MVP1 -- no existe hoy un endpoint que
 *   exporte el `VectorDocument` multicapa completo como un solo archivo, y
 *   crear uno no fue pedido por esta tarjeta (fuera de alcance: "no
 *   inventes que algo funciona").
 */
export function EditorHeader({ projectName, onClose, saveState, saveErrorMessage, canSave, onSave }: EditorHeaderProps) {
  const isSaving = saveState === "saving";

  return (
    <header className="editor-header">
      <div className="editor-header__brand">
        <span className="editor-header__logo">VECTORiZE</span>
        <button type="button" className="editor-header__back" onClick={onClose} aria-label="Volver a la lista de proyectos">
          ← Projects
        </button>
        <span className="editor-header__project-name">{projectName}</span>
        <span
          className={`editor-header__dirty-state editor-header__dirty-state--${saveState}`}
          role="status"
        >
          {saveState === "error" && saveErrorMessage ? saveErrorMessage : DIRTY_STATE_COPY[saveState]}
        </span>
      </div>

      <div className="editor-header__actions">
        <button type="button" className="editor-header__button" disabled aria-label="Deshacer (llega en MVP3)" title="Deshacer — llega en MVP3">
          ↶
        </button>
        <button type="button" className="editor-header__button" disabled aria-label="Rehacer (llega en MVP3)" title="Rehacer — llega en MVP3">
          ↷
        </button>
        <button
          type="button"
          className="editor-header__button editor-header__button--primary"
          onClick={onSave}
          disabled={!canSave || isSaving}
          aria-label={saveState === "error" ? "Reintentar guardar" : "Guardar"}
          title={saveState === "error" ? "Reintentar guardar" : "Guardar"}
        >
          {isSaving ? "GUARDANDO…" : saveState === "error" ? "REINTENTAR" : "SAVE"}
        </button>
        <button
          type="button"
          className="editor-header__button"
          disabled
          aria-label="Exportar el documento completo (próximamente)"
          title="Exportar el documento completo — próximamente"
        >
          EXPORT
        </button>
      </div>
    </header>
  );
}
