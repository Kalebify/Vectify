interface EditorHeaderProps {
  projectName: string;
  onClose: () => void;
}

/**
 * Header del wireframe obligatorio: "VECTORiZE ← Projects Project.svg ✓ ↶
 * ↷ SAVE EXPORT".
 *
 * Decisiones documentadas en IMPL.md (spec.md, "Ambigüedades detectadas"):
 * - **Dirty/saved**: el wireframe muestra un check ("✓") como si el
 *   documento estuviera siempre guardado. Esta tarjeta NO persiste el
 *   `VectorDocument` (esa es M2.1-S08, la tarjeta siguiente) -- mostrar un
 *   check fijo sería un dato falso (DoD: "no hay datos falsos"). En su
 *   lugar, un indicador honesto: "Sin guardado automático todavía".
 * - **Undo/Redo**: placeholders deshabilitados, sin funcionalidad (esta
 *   tarjeta no implementa historial de edición).
 * - **Save**: placeholder deshabilitado -- la persistencia real es
 *   M2.1-S08.
 * - **Export**: placeholder deshabilitado. `ExportPanel` (M1-S10) exporta
 *   UN `VectorVersion`/`SimplificationVersion`/`DimensionVersion` del
 *   pipeline de un solo vector de MVP1 -- no existe hoy un endpoint que
 *   exporte el `VectorDocument` multicapa completo como un solo archivo, y
 *   crear uno no fue pedido por esta tarjeta (fuera de alcance: "no
 *   inventes que algo funciona"). Reutilizarlo tal cual exportaría solo UNA
 *   capa a la vez fuera de contexto, lo cual sería confuso, no "natural".
 */
export function EditorHeader({ projectName, onClose }: EditorHeaderProps) {
  return (
    <header className="editor-header">
      <div className="editor-header__brand">
        <span className="editor-header__logo">VECTORiZE</span>
        <button type="button" className="editor-header__back" onClick={onClose} aria-label="Volver a la lista de proyectos">
          ← Projects
        </button>
        <span className="editor-header__project-name">{projectName}</span>
        <span className="editor-header__dirty-state" role="status">
          Sin guardado automático todavía
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
          disabled
          aria-label="Guardar (la persistencia real llega en M2.1-S08)"
          title="Guardar — la persistencia real llega en M2.1-S08"
        >
          SAVE
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
