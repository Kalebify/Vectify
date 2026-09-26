import { useCallback, useEffect, useRef, useState } from "react";
import { generateVectorComponents } from "../api/componentsApi";
import { ApiClientError } from "../api/httpClient";
import type { ApiErrorResponse } from "../types/preprocess";
import type { LayerComponentPayload } from "../types/components";
import type { VectorLayerPayload } from "../types/vectorLayers";

/**
 * Estados del análisis de componentes por capa (M2-S03): igual criterio de
 * disparo manual que useCheck (M1-S08) -- el usuario pide el cálculo
 * explícitamente ("Calcular componentes"), nunca automático al generar las
 * capas. "ready" puede convivir con un `errorMessage` no nulo si alguna(s)
 * capa(s) fallaron pero al menos una se calculó con éxito -- ver `compute`.
 */
export type LayerComponentsStatus = "idle" | "loading" | "ready" | "error";

const GENERIC_ERROR_MESSAGE = "No se pudieron calcular los componentes de una o más capas. Intentá de nuevo.";

export interface SelectedComponent {
  groupId: string;
  componentId: string;
}

export interface UseLayerComponentsState {
  status: LayerComponentsStatus;
  errorMessage: string | null;
  /** groupId -> componentes calculados para esa capa (ausente si todavía no se calculó, o si falló). */
  componentsByGroup: Record<string, LayerComponentPayload[]>;
  selected: SelectedComponent | null;
  /** Calcula (o vuelve a pedir, reutilizando desde caché del lado de la Web API si el VectorId no cambió) los componentes de TODAS las capas del conjunto, en paralelo. */
  compute: () => void;
  /** Selección bidireccional: click de nuevo sobre el mismo componente lo deselecciona. */
  select: (groupId: string, componentId: string) => void;
  clearSelection: () => void;
}

export function useLayerComponents(
  projectId: string,
  imageId: string,
  layers: VectorLayerPayload[],
): UseLayerComponentsState {
  const [status, setStatus] = useState<LayerComponentsStatus>("idle");
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [componentsByGroup, setComponentsByGroup] = useState<Record<string, LayerComponentPayload[]>>({});
  const [selected, setSelected] = useState<SelectedComponent | null>(null);

  const abortControllerRef = useRef<AbortController | null>(null);

  useEffect(() => {
    return () => abortControllerRef.current?.abort();
  }, []);

  const compute = useCallback(() => {
    if (layers.length === 0) {
      return;
    }

    abortControllerRef.current?.abort();
    const controller = new AbortController();
    abortControllerRef.current = controller;

    setStatus("loading");
    setErrorMessage(null);
    setSelected(null);

    Promise.allSettled(
      layers.map((layer) =>
        generateVectorComponents(projectId, imageId, layer.vectorId, controller.signal).then((response) => ({
          groupId: layer.groupId,
          components: response.components,
        })),
      ),
    ).then((results) => {
      if (controller.signal.aborted) {
        return;
      }
      abortControllerRef.current = null;

      const nextComponentsByGroup: Record<string, LayerComponentPayload[]> = {};
      let firstErrorMessage: string | null = null;

      for (const result of results) {
        if (result.status === "fulfilled") {
          nextComponentsByGroup[result.value.groupId] = result.value.components;
        } else if (firstErrorMessage === null && !(result.reason instanceof ApiClientError && result.reason.isAborted)) {
          firstErrorMessage = describeError(result.reason);
        }
      }

      setComponentsByGroup(nextComponentsByGroup);
      if (Object.keys(nextComponentsByGroup).length === 0) {
        setErrorMessage(firstErrorMessage ?? GENERIC_ERROR_MESSAGE);
        setStatus("error");
      } else {
        // Éxito parcial: al menos una capa se calculó -- se muestra igual,
        // con el mensaje de error de la(s) que fallaron como advertencia no
        // bloqueante (ver docstring del tipo LayerComponentsStatus).
        setErrorMessage(firstErrorMessage);
        setStatus("ready");
      }
    });
  }, [projectId, imageId, layers]);

  const select = useCallback((groupId: string, componentId: string) => {
    setSelected((current) =>
      current?.groupId === groupId && current?.componentId === componentId ? null : { groupId, componentId },
    );
  }, []);

  const clearSelection = useCallback(() => setSelected(null), []);

  return { status, errorMessage, componentsByGroup, selected, compute, select, clearSelection };
}

function describeError(reason: unknown): string {
  if (reason instanceof ApiClientError && reason.body) {
    const body = reason.body as Partial<ApiErrorResponse>;
    return body.message ?? GENERIC_ERROR_MESSAGE;
  }
  if (reason instanceof ApiClientError && reason.isNetworkError) {
    return "No se pudo contactar a la Web API para calcular los componentes.";
  }
  return GENERIC_ERROR_MESSAGE;
}
