import { useState } from "react";
import type { ColorGroupPayload } from "../../types/colorPalette";

interface ColorSwatchListProps {
  groups: ColorGroupPayload[];
  selectedGroupIds: string[];
  disabled: boolean;
  onToggleSelection: (groupId: string) => void;
  onRename: (groupId: string, name: string) => void;
  onUnmerge: (groupId: string) => void;
}

/**
 * Paleta interactiva (spec.md M2-S01, "React"): swatches con color, nombre
 * editable, porcentaje de área aproximada, selección múltiple (checkbox)
 * para fusionar, y acción de deshacer fusión por grupo. Cada swatch es
 * independiente: renombrar uno no afecta la selección de los demás.
 */
export function ColorSwatchList({
  groups,
  selectedGroupIds,
  disabled,
  onToggleSelection,
  onRename,
  onUnmerge,
}: ColorSwatchListProps) {
  return (
    <ul className="color-swatch-list" aria-label="Colores detectados">
      {groups.map((group) => (
        <ColorSwatchRow
          key={group.groupId}
          group={group}
          selected={selectedGroupIds.includes(group.groupId)}
          disabled={disabled}
          onToggleSelection={onToggleSelection}
          onRename={onRename}
          onUnmerge={onUnmerge}
        />
      ))}
    </ul>
  );
}

interface ColorSwatchRowProps {
  group: ColorGroupPayload;
  selected: boolean;
  disabled: boolean;
  onToggleSelection: (groupId: string) => void;
  onRename: (groupId: string, name: string) => void;
  onUnmerge: (groupId: string) => void;
}

function ColorSwatchRow({ group, selected, disabled, onToggleSelection, onRename, onUnmerge }: ColorSwatchRowProps) {
  const [draftName, setDraftName] = useState(group.name);

  // El nombre del grupo puede cambiar por fuera (otra edición trajo una
  // versión nueva de la paleta con el mismo GroupId, ej. tras confirmar) --
  // se resincroniza durante el render (patrón recomendado de React para
  // "ajustar estado cuando cambia una prop", mismo criterio que CheckPanel
  // al cambiar de fuente), no en un efecto.
  const [lastSyncedName, setLastSyncedName] = useState(group.name);
  if (group.name !== lastSyncedName) {
    setLastSyncedName(group.name);
    setDraftName(group.name);
  }

  const commitRename = () => {
    const trimmed = draftName.trim();
    if (trimmed && trimmed !== group.name) {
      onRename(group.groupId, trimmed);
    } else {
      setDraftName(group.name);
    }
  };

  return (
    <li className="color-swatch" aria-label={`Grupo de color ${group.name}`}>
      <label className="color-swatch__select">
        <input
          type="checkbox"
          checked={selected}
          disabled={disabled}
          onChange={() => onToggleSelection(group.groupId)}
          aria-label={`Seleccionar ${group.name} para fusionar`}
        />
        <span
          className="color-swatch__color"
          style={{ backgroundColor: group.colorHex }}
          aria-hidden="true"
          title={group.colorHex}
        />
      </label>

      <input
        id={`color-swatch-name-${group.groupId}`}
        className="color-swatch__name"
        type="text"
        value={draftName}
        disabled={disabled}
        onChange={(event) => setDraftName(event.target.value)}
        onBlur={commitRename}
        onKeyDown={(event) => {
          if (event.key === "Enter") {
            event.currentTarget.blur();
          }
        }}
        aria-label={`Nombre del grupo ${group.name}`}
      />

      <span className="color-swatch__area">{group.areaPercent.toFixed(1)}%</span>

      {group.hasPartialAlpha && (
        <span className="color-swatch__badge" title="Parte de este grupo tiene transparencia parcial">
          semi-transparente
        </span>
      )}

      {group.isMerged && (
        <button
          type="button"
          className="upload-actions__button color-swatch__unmerge"
          disabled={disabled}
          onClick={() => onUnmerge(group.groupId)}
        >
          Deshacer fusión
        </button>
      )}
    </li>
  );
}
