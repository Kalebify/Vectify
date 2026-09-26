import { useState } from "react";
import type { GroupSelection } from "../../hooks/useComponentGroups";
import type { SelectedComponent } from "../../hooks/useLayerComponents";
import type { ComponentGroupPayload } from "../../types/componentGroups";
import type { LayerComponentPayload } from "../../types/components";
import type { VectorLayerPayload } from "../../types/vectorLayers";

interface ComponentTreeProps {
  layers: VectorLayerPayload[];
  componentsByGroup: Record<string, LayerComponentPayload[]>;
  selected: SelectedComponent | null;
  onSelect: (groupId: string, componentId: string) => void;

  /** Agrupación lógica de componentes (M2-S05) -- todos opcionales para que ComponentTree siga funcionando sin ellos. */
  groupsByLayer?: Record<string, ComponentGroupPayload[]>;
  selection?: GroupSelection | null;
  onToggleComponentSelection?: (layerGroupId: string, componentId: string) => void;
  onCreateGroup?: (layerGroupId: string, vectorId: string) => void;
  mutatingLayerGroupId?: string | null;
  highlightedGroupId?: string | null;
  onSelectGroupAsSet?: (layerGroupId: string, groupId: string) => void;
  onUngroup?: (layerGroupId: string, vectorId: string, groupId: string) => void;
  onRenameGroup?: (layerGroupId: string, vectorId: string, groupId: string, name: string) => void;
}

function pieceCountLabel(count: number): string {
  return `${count} ${count === 1 ? "pieza" : "piezas"}`;
}

/**
 * Árbol "Layer -> Components" de spec.md M2-S03, extendido por M2-S05 con
 * agrupación lógica: cada layer expande a sus componentes físicos (ej.
 * "Azul: 3 piezas"), con selección -- cada componente es un `<button>` (no
 * un `<div>` con onClick) que notifica al padre para resaltarlo en
 * LayerCanvas y viceversa (ver ComponentOverlay). Solo muestra las capas
 * cuyos componentes YA se calcularon (ver useLayerComponents.compute) -- las
 * que todavía no, simplemente no aparecen en el árbol todavía.
 *
 * M2-S05: cada pieza suma un checkbox de selección múltiple (confinada a UNA
 * capa a la vez -- un ComponentGroup solo puede referenciar componentIds de
 * la ComponentSetVersion de UN VectorId, ver spec.md, criterio de
 * aceptación), un botón "Agrupar" que aparece con 2+ piezas seleccionadas de
 * la MISMA capa, y -- si ya hay grupos persistidos -- una lista "Grupos" con
 * nombre editable, "seleccionar como conjunto" (resalta todos sus miembros a
 * la vez, ver useComponentGroups) y "Desagrupar".
 */
export function ComponentTree({
  layers,
  componentsByGroup,
  selected,
  onSelect,
  groupsByLayer,
  selection,
  onToggleComponentSelection,
  onCreateGroup,
  mutatingLayerGroupId,
  highlightedGroupId,
  onSelectGroupAsSet,
  onUngroup,
  onRenameGroup,
}: ComponentTreeProps) {
  const layersWithComponents = layers.filter((layer) => componentsByGroup[layer.groupId] !== undefined);

  if (layersWithComponents.length === 0) {
    return null;
  }

  return (
    <ul className="component-tree" aria-label="Árbol de capas y sus componentes físicos">
      {layersWithComponents.map((layer) => {
        const components = componentsByGroup[layer.groupId]!;
        const groups = groupsByLayer?.[layer.groupId] ?? [];
        const layerSelection = selection?.layerGroupId === layer.groupId ? selection : null;
        const isMutating = mutatingLayerGroupId === layer.groupId;

        return (
          <li key={layer.groupId} className="component-tree__layer">
            <div className="component-tree__layer-header">
              <span className="layer-row__color" style={{ backgroundColor: layer.colorHex }} aria-hidden="true" />
              <span className="component-tree__layer-name">
                {layer.name}: {pieceCountLabel(components.length)}
              </span>
            </div>

            <ul className="component-tree__components" aria-label={`Componentes de la capa ${layer.name}`}>
              {components.map((component, index) => {
                const isSelected = selected?.groupId === layer.groupId && selected?.componentId === component.id;
                const isCheckedForGrouping = layerSelection?.componentIds.includes(component.id) ?? false;
                const pieceLabel = `Pieza ${index + 1} de ${layer.name}${component.isTiny ? " (diminuta)" : ""}`;
                return (
                  <li key={component.id} className="component-tree__component-row">
                    {onToggleComponentSelection && (
                      <input
                        type="checkbox"
                        checked={isCheckedForGrouping}
                        disabled={isMutating}
                        onChange={() => onToggleComponentSelection(layer.groupId, component.id)}
                        aria-label={`Seleccionar ${pieceLabel} para agrupar`}
                      />
                    )}
                    <button
                      type="button"
                      className="component-tree__item"
                      aria-pressed={isSelected}
                      aria-label={pieceLabel}
                      onClick={() => onSelect(layer.groupId, component.id)}
                    >
                      <span className="component-tree__item-name">Pieza {index + 1}</span>
                      {component.isTiny && <span className="component-tree__tiny-badge">diminuta</span>}
                    </button>
                  </li>
                );
              })}
            </ul>

            {onCreateGroup && layerSelection && layerSelection.componentIds.length >= 2 && (
              <button
                type="button"
                className="upload-actions__button component-tree__group-action"
                disabled={isMutating}
                onClick={() => onCreateGroup(layer.groupId, layer.vectorId)}
              >
                Agrupar {layerSelection.componentIds.length} piezas seleccionadas
              </button>
            )}

            {groups.length > 0 && (
              <ul className="component-tree__groups" aria-label={`Grupos de la capa ${layer.name}`}>
                {groups.map((group) => (
                  <ComponentGroupRow
                    key={group.groupId}
                    group={group}
                    layerName={layer.name}
                    isHighlighted={highlightedGroupId === group.groupId}
                    disabled={isMutating}
                    onSelectAsSet={onSelectGroupAsSet ? () => onSelectGroupAsSet(layer.groupId, group.groupId) : undefined}
                    onUngroup={onUngroup ? () => onUngroup(layer.groupId, layer.vectorId, group.groupId) : undefined}
                    onRename={
                      onRenameGroup ? (name: string) => onRenameGroup(layer.groupId, layer.vectorId, group.groupId, name) : undefined
                    }
                  />
                ))}
              </ul>
            )}
          </li>
        );
      })}
    </ul>
  );
}

interface ComponentGroupRowProps {
  group: ComponentGroupPayload;
  layerName: string;
  isHighlighted: boolean;
  disabled: boolean;
  onSelectAsSet?: () => void;
  onUngroup?: () => void;
  onRename?: (name: string) => void;
}

/**
 * Una fila de ComponentGroup: nombre editable (mismo patrón de "input +
 * onBlur/Enter confirma" que ColorSwatchRow, M2-S01), "seleccionar como
 * conjunto" (resalta TODOS sus componentes miembro a la vez -- ver
 * useComponentGroups.selectGroupAsSet) y "Desagrupar". Si `group.isStale`,
 * avisa sin romper que uno o más componentIds referenciados ya no existen
 * en el análisis vigente (ver Vectify.Api.Components.ComponentGroupStaleness) --
 * el grupo sigue siendo válido para su propia versión, nunca se borra solo.
 */
function ComponentGroupRow({ group, layerName, isHighlighted, disabled, onSelectAsSet, onUngroup, onRename }: ComponentGroupRowProps) {
  const [draftName, setDraftName] = useState(group.name);

  // Igual criterio que ColorSwatchRow: resincroniza durante el render si el
  // nombre cambió por fuera (otra edición trajo una versión nueva del
  // conjunto de grupos con el mismo GroupId).
  const [lastSyncedName, setLastSyncedName] = useState(group.name);
  if (group.name !== lastSyncedName) {
    setLastSyncedName(group.name);
    setDraftName(group.name);
  }

  const commitRename = () => {
    const trimmed = draftName.trim();
    if (trimmed && trimmed !== group.name) {
      onRename?.(trimmed);
    } else {
      setDraftName(group.name);
    }
  };

  return (
    <li className="component-tree__group" aria-label={`Grupo ${group.name} de la capa ${layerName}`}>
      <button
        type="button"
        className="component-tree__group-select"
        aria-pressed={isHighlighted}
        aria-label={`Seleccionar como conjunto: ${group.name} (${pieceCountLabel(group.componentIds.length)})`}
        disabled={disabled || !onSelectAsSet}
        onClick={onSelectAsSet}
      >
        {group.name} ({pieceCountLabel(group.componentIds.length)})
      </button>

      <input
        type="text"
        className="component-tree__group-name"
        value={draftName}
        disabled={disabled || !onRename}
        onChange={(event) => setDraftName(event.target.value)}
        onBlur={commitRename}
        onKeyDown={(event) => {
          if (event.key === "Enter") {
            event.currentTarget.blur();
          }
        }}
        aria-label={`Nombre del grupo ${group.name}`}
      />

      <button
        type="button"
        className="upload-actions__button component-tree__group-ungroup"
        disabled={disabled || !onUngroup}
        onClick={onUngroup}
      >
        Desagrupar
      </button>

      {group.isStale && (
        <span className="component-tree__group-stale" role="note">
          Uno o más componentes de este grupo ya no existen en el análisis vigente de esta capa.
        </span>
      )}
    </li>
  );
}
