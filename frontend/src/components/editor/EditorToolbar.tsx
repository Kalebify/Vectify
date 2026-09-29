export type EditorTool = "select" | "pan";

interface ToolDefinition {
  id: EditorTool | string;
  icon: string;
  label: string;
  enabled: boolean;
  shortcut?: string;
}

/**
 * Shell COMPLETO de herramientas del wireframe obligatorio de spec.md
 * (Select, Pan, Crop, Move, Fill, Color, Draw, Erase, Offset, Cut, Path):
 * TODAS presentes y visibles (nunca ocultas), pero solo Select/Pan
 * funcionales en esta tarjeta -- el resto queda deshabilitado con
 * `disabled` + `title`/`aria-describedby` indicando que llega en MVP3 (spec.md,
 * "Fuera de alcance": nunca implementar Crop/Draw/Erase/Boolean/Nodes/
 * Bridges/IA, ni siquiera parcialmente).
 */
const TOOLS: ToolDefinition[] = [
  { id: "select", icon: "↖", label: "Select", enabled: true, shortcut: "V" },
  { id: "pan", icon: "✋", label: "Pan", enabled: true, shortcut: "Espacio (mantener) o H" },
  { id: "crop", icon: "✂", label: "Crop", enabled: false },
  { id: "move", icon: "↔", label: "Move", enabled: false },
  { id: "fill", icon: "▣", label: "Fill", enabled: false },
  { id: "color", icon: "◉", label: "Color", enabled: false },
  { id: "draw", icon: "✎", label: "Draw", enabled: false },
  { id: "erase", icon: "⌫", label: "Erase", enabled: false },
  { id: "offset", icon: "⤢", label: "Offset", enabled: false },
  { id: "cut", icon: "✂", label: "Cut", enabled: false },
  { id: "path", icon: "⌁", label: "Path", enabled: false },
];

interface EditorToolbarProps {
  activeTool: EditorTool;
  onSelectTool: (tool: EditorTool) => void;
}

export function EditorToolbar({ activeTool, onSelectTool }: EditorToolbarProps) {
  return (
    <nav className="editor-toolbar" aria-label="Herramientas del editor">
      <ul className="editor-toolbar__list">
        {TOOLS.map((tool) => {
          const isActive = tool.enabled && tool.id === activeTool;
          const title = tool.enabled
            ? tool.shortcut
              ? `${tool.label} (${tool.shortcut})`
              : tool.label
            : `${tool.label} — llega en MVP3`;

          return (
            <li key={tool.id}>
              <button
                type="button"
                className={`editor-toolbar__button${isActive ? " editor-toolbar__button--active" : ""}`}
                disabled={!tool.enabled}
                aria-pressed={tool.enabled ? isActive : undefined}
                aria-label={title}
                title={title}
                onClick={() => {
                  if (tool.enabled) onSelectTool(tool.id as EditorTool);
                }}
              >
                <span aria-hidden="true" className="editor-toolbar__icon">
                  {tool.icon}
                </span>
                <span className="editor-toolbar__label">{tool.label}</span>
              </button>
            </li>
          );
        })}
      </ul>
    </nav>
  );
}
