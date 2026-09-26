import { useCallback, useEffect, useRef, useState } from "react";
import { getComponentGroups, groupComponents, renameComponentGroup, ungroupComponents } from "../api/componentGroupsApi";
import { ApiClientError } from "../api/httpClient";
import type { ApiErrorResponse } from "../types/preprocess";
import type { ComponentGroupErrorCode, ComponentGroupPayload } from "../types/componentGroups";
import type { LayerComponentPayload } from "../types/components";
import type { VectorLayerPayload } from "../types/vectorLayers";

const GENERIC_ERROR_MESSAGE = "No se pudo completar la operación sobre el grupo de componentes. Intentá de nuevo.";

const KNOWN_ERROR_CODES: ComponentGroupErrorCode[] = [
  "not_found",
  "component_not_found",
  "group_not_found",
  "invalid_parameters",
  "internal_error",
  "network_error",
];

function isKnownErrorCode(code: string): code is ComponentGroupErrorCode {
  return (KNOWN_ERROR_CODES as string[]).includes(code);
}

/** Selección múltiple para agrupar -- confinada a UNA capa a la vez (un ComponentGroup solo puede referenciar componentIds de la ComponentSetVersion de UN VectorId). */
export interface GroupSelection {
  layerGroupId: string;
  componentIds: string[];
}

/** Grupo actualmente resaltado "como conjunto" -- ver interpretación de "mover/seleccionar como conjunto" en spec.md, "Ambigüedades detectadas". */
export interface HighlightedGroup {
  layerGroupId: string;
  groupId: string;
  componentIds: string[];
}

export interface UseComponentGroupsState {
  /** layer.groupId -> grupos lógicos vigentes de esa capa (ausente si todavía no se consultaron). */
  groupsByLayer: Record<string, ComponentGroupPayload[]>;
  selection: GroupSelection | null;
  highlighted: HighlightedGroup | null;
  errorCode: ComponentGroupErrorCode | null;
  errorMessage: string | null;
  /** layer.groupId de la capa cuyo conjunto de grupos se está mutando (agrupar/desagrupar/renombrar en curso), para deshabilitar sus controles. */
  mutatingLayerGroupId: string | null;
  toggleComponentSelection: (layerGroupId: string, componentId: string) => void;
  clearSelection: () => void;
  createGroup: (layerGroupId: string, vectorId: string) => void;
  ungroup: (layerGroupId: string, vectorId: string, groupId: string) => void;
  rename: (layerGroupId: string, vectorId: string, groupId: string, name: string) => void;
  /** Alterna el resaltado "como conjunto" de un grupo: clickear el mismo grupo de nuevo lo apaga. */
  selectGroupAsSet: (layerGroupId: string, groupId: string) => void;
}

/**
 * Orquesta la agrupación LÓGICA de componentes físicos (M2-S05): multi-select
 * confinado a UNA capa a la vez (ver spec.md, criterio de aceptación: "no
 * debe poder crearse un grupo nuevo mezclando IDs de dos versiones
 * distintas" -- cada ComponentSetVersion es de UN VectorId, así que mezclar
 * capas siempre mezclaría versiones distintas), agrupar/desagrupar/renombrar
 * (ediciones de metadata puras, nunca tocan paths ni VectorVersion/
 * ComponentSetVersion), y "seleccionar como conjunto": clickear un grupo en
 * el árbol resalta TODOS sus componentes miembro a la vez (interpretación de
 * "mover/seleccionar como conjunto cuando el editor lo permita" -- este
 * proyecto no tiene un editor de manipulación directa de geometría, ver
 * spec.md, "Ambigüedades detectadas"). Recupera automáticamente los grupos
 * YA persistidos de cada capa apenas sus componentes (M2-S03) están
 * calculados -- sin bloquear ni recalcular nada de useLayerComponents.
 */
export function useComponentGroups(
  projectId: string,
  imageId: string,
  layers: VectorLayerPayload[],
  componentsByGroup: Record<string, LayerComponentPayload[]>,
): UseComponentGroupsState {
  const [groupsByLayer, setGroupsByLayer] = useState<Record<string, ComponentGroupPayload[]>>({});
  const [selection, setSelection] = useState<GroupSelection | null>(null);
  const [highlighted, setHighlighted] = useState<HighlightedGroup | null>(null);
  const [errorCode, setErrorCode] = useState<ComponentGroupErrorCode | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [mutatingLayerGroupId, setMutatingLayerGroupId] = useState<string | null>(null);

  const fetchedLayerIdsRef = useRef<Set<string>>(new Set());
  const abortControllersRef = useRef<Set<AbortController>>(new Set());

  useEffect(() => {
    const controllers = abortControllersRef.current;
    return () => {
      for (const controller of controllers) {
        controller.abort();
      }
    };
  }, []);

  // Apenas una capa tiene componentes calculados (M2-S03), recupera -- una
  // sola vez -- sus grupos lógicos YA persistidos, si los hubiera. No
  // depende de que el usuario haga nada más: los grupos son datos
  // persistidos, no una acción que el usuario deba disparar de nuevo.
  useEffect(() => {
    for (const layer of layers) {
      if (componentsByGroup[layer.groupId] === undefined) {
        continue;
      }
      if (fetchedLayerIdsRef.current.has(layer.groupId)) {
        continue;
      }
      fetchedLayerIdsRef.current.add(layer.groupId);

      const controller = new AbortController();
      abortControllersRef.current.add(controller);

      getComponentGroups(projectId, imageId, layer.vectorId, controller.signal)
        .then((response) => {
          abortControllersRef.current.delete(controller);
          setGroupsByLayer((current) => ({ ...current, [layer.groupId]: response.groups }));
        })
        .catch((error: unknown) => {
          abortControllersRef.current.delete(controller);
          if (error instanceof ApiClientError && error.isAborted) {
            return;
          }
          // No bloquea el árbol de componentes ya calculados: reintenta en el próximo render con datos nuevos.
          fetchedLayerIdsRef.current.delete(layer.groupId);
        });
    }
  }, [projectId, imageId, layers, componentsByGroup]);

  const handleError = useCallback((error: unknown) => {
    if (error instanceof ApiClientError && error.isAborted) {
      return;
    }

    if (error instanceof ApiClientError && error.body) {
      const body = error.body as Partial<ApiErrorResponse>;
      const code = typeof body.code === "string" && isKnownErrorCode(body.code) ? body.code : "internal_error";
      setErrorCode(code);
      setErrorMessage(body.message ?? GENERIC_ERROR_MESSAGE);
    } else if (error instanceof ApiClientError && error.isNetworkError) {
      setErrorCode("network_error");
      setErrorMessage("No se pudo contactar a la Web API para operar sobre los grupos de componentes.");
    } else {
      setErrorCode("internal_error");
      setErrorMessage(GENERIC_ERROR_MESSAGE);
    }
  }, []);

  const toggleComponentSelection = useCallback((layerGroupId: string, componentId: string) => {
    setSelection((current) => {
      if (!current || current.layerGroupId !== layerGroupId) {
        return { layerGroupId, componentIds: [componentId] };
      }

      const nextIds = current.componentIds.includes(componentId)
        ? current.componentIds.filter((id) => id !== componentId)
        : [...current.componentIds, componentId];

      return nextIds.length === 0 ? null : { layerGroupId, componentIds: nextIds };
    });
  }, []);

  const clearSelection = useCallback(() => setSelection(null), []);

  const createGroup = useCallback(
    (layerGroupId: string, vectorId: string) => {
      if (!selection || selection.layerGroupId !== layerGroupId || selection.componentIds.length < 2) {
        return;
      }

      const componentIds = selection.componentIds;
      setErrorCode(null);
      setErrorMessage(null);
      setMutatingLayerGroupId(layerGroupId);
      setSelection(null);

      groupComponents(projectId, imageId, vectorId, componentIds, null)
        .then((response) => {
          setMutatingLayerGroupId(null);
          setGroupsByLayer((current) => ({ ...current, [layerGroupId]: response.groups }));
        })
        .catch((error: unknown) => {
          setMutatingLayerGroupId(null);
          handleError(error);
        });
    },
    [projectId, imageId, selection, handleError],
  );

  const ungroup = useCallback(
    (layerGroupId: string, vectorId: string, groupId: string) => {
      setErrorCode(null);
      setErrorMessage(null);
      setMutatingLayerGroupId(layerGroupId);

      ungroupComponents(projectId, imageId, vectorId, groupId)
        .then((response) => {
          setMutatingLayerGroupId(null);
          setGroupsByLayer((current) => ({ ...current, [layerGroupId]: response.groups }));
          setHighlighted((current) => (current?.groupId === groupId ? null : current));
        })
        .catch((error: unknown) => {
          setMutatingLayerGroupId(null);
          handleError(error);
        });
    },
    [projectId, imageId, handleError],
  );

  const rename = useCallback(
    (layerGroupId: string, vectorId: string, groupId: string, name: string) => {
      setErrorCode(null);
      setErrorMessage(null);
      setMutatingLayerGroupId(layerGroupId);

      renameComponentGroup(projectId, imageId, vectorId, groupId, name)
        .then((response) => {
          setMutatingLayerGroupId(null);
          setGroupsByLayer((current) => ({ ...current, [layerGroupId]: response.groups }));
          setHighlighted((current) => {
            if (current?.groupId !== groupId) {
              return current;
            }
            const renamedGroup = response.groups.find((group) => group.groupId === groupId);
            return renamedGroup ? { ...current, componentIds: renamedGroup.componentIds } : current;
          });
        })
        .catch((error: unknown) => {
          setMutatingLayerGroupId(null);
          handleError(error);
        });
    },
    [projectId, imageId, handleError],
  );

  const selectGroupAsSet = useCallback(
    (layerGroupId: string, groupId: string) => {
      setHighlighted((current) => {
        if (current?.groupId === groupId) {
          return null;
        }

        const group = groupsByLayer[layerGroupId]?.find((candidate) => candidate.groupId === groupId);
        return group ? { layerGroupId, groupId, componentIds: group.componentIds } : current;
      });
    },
    [groupsByLayer],
  );

  return {
    groupsByLayer,
    selection,
    highlighted,
    errorCode,
    errorMessage,
    mutatingLayerGroupId,
    toggleComponentSelection,
    clearSelection,
    createGroup,
    ungroup,
    rename,
    selectGroupAsSet,
  };
}
