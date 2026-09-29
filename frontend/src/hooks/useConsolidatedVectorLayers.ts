import { useCallback, useEffect, useRef, useState } from "react";
import { getConsolidatedVectorLayers } from "../api/consolidatedVectorLayersApi";
import { ApiClientError } from "../api/httpClient";
import type { ConsolidatedVectorLayerPayload } from "../types/consolidatedVectorLayers";

export type ConsolidatedVectorLayersStatus = "idle" | "loading" | "ready" | "error";

const GENERIC_ERROR_MESSAGE = "No se pudo obtener la información consolidada de las capas.";

export interface UseConsolidatedVectorLayersState {
  status: ConsolidatedVectorLayersStatus;
  /** groupId -> payload consolidado (M2.1-S03/M2.1-S04: fill, pathCount, componentCount, manufacturingOperation, rasterValidation). */
  layersById: Record<string, ConsolidatedVectorLayerPayload>;
  errorMessage: string | null;
  /** Vuelve a pedir el consolidado explícitamente (ej. después de calcular componentes o asignar una operación de fabricación). */
  refetch: () => void;
}

function toLayersById(layers: ConsolidatedVectorLayerPayload[]): Record<string, ConsolidatedVectorLayerPayload> {
  return Object.fromEntries(layers.map((layer) => [layer.id, layer]));
}

/**
 * Fuente de "Información visible por layer" de spec.md M2.1-S04 (nombre,
 * HEX, número de paths, número de componentes físicos, visibilidad,
 * operación de fabricación): en vez de recombinar a mano lo que ya calculan
 * useVectorLayers/useLayerComponents/useManufacturingOperations, este hook
 * lee el endpoint CONSOLIDADO de M2.1-S03 (GET .../layers/consolidated) --
 * de solo lectura, nunca dispara ningún cálculo.
 *
 * Se recupera automáticamente una vez por `layerSetId` (mismo criterio de
 * "recuperar apenas hay algo que recuperar" que useManufacturingOperations,
 * con la lógica de fetch inline en el efecto, no delegada a un callback
 * externo -- así el linter puede verificar que el efecto sincroniza con un
 * sistema externo en vez de disparar un setState indirecto), y expone
 * `refetch` (un callback SEPARADO, con la misma llamada pero sin vivir
 * dentro de ningún efecto) para que quien lo use pueda refrescar
 * explícitamente después de una acción que sabe que cambió el lado
 * consolidado (calcular componentes, asignar una operación) -- un botón
 * "Actualizar info" en vez de refetchear en cada render de
 * `componentsByGroup`/`operations` (evitaría loops de fetch por cambios de
 * identidad de esos objetos que no siempre representan un cambio real).
 */
export function useConsolidatedVectorLayers(
  projectId: string,
  imageId: string,
  paletteId: string | null,
  layerSetId: string | null,
): UseConsolidatedVectorLayersState {
  const [status, setStatus] = useState<ConsolidatedVectorLayersStatus>("idle");
  const [layersById, setLayersById] = useState<Record<string, ConsolidatedVectorLayerPayload>>({});
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const abortControllerRef = useRef<AbortController | null>(null);
  const fetchedLayerSetIdRef = useRef<string | null>(null);

  useEffect(() => {
    return () => abortControllerRef.current?.abort();
  }, []);

  // Recupera -- una sola vez por layerSetId -- apenas el conjunto de capas
  // (M2-S02) está disponible. Un layerSetId nuevo (paleta recalculada)
  // dispara un fetch nuevo automáticamente (misma identidad de dependencia).
  useEffect(() => {
    if (!paletteId || !layerSetId) {
      return;
    }
    if (fetchedLayerSetIdRef.current === layerSetId) {
      return;
    }
    fetchedLayerSetIdRef.current = layerSetId;

    abortControllerRef.current?.abort();
    const controller = new AbortController();
    abortControllerRef.current = controller;

    setStatus("loading");
    setErrorMessage(null);

    getConsolidatedVectorLayers(projectId, imageId, paletteId, controller.signal)
      .then((response) => {
        abortControllerRef.current = null;
        setLayersById(toLayersById(response.layers));
        setStatus("ready");
      })
      .catch((error: unknown) => {
        abortControllerRef.current = null;
        if (error instanceof ApiClientError && error.isAborted) {
          return;
        }
        setStatus("error");
        setErrorMessage(GENERIC_ERROR_MESSAGE);
      });
  }, [projectId, imageId, paletteId, layerSetId]);

  // Refresco explícito (nunca disparado automáticamente desde un efecto):
  // misma llamada que arriba, pero invocable en respuesta a una acción del
  // usuario o a que otro hook (ej. useLayerComponents) haya terminado de
  // recalcular algo que el consolidado todavía no refleja.
  const refetch = useCallback(() => {
    if (!paletteId || !layerSetId) {
      return;
    }

    abortControllerRef.current?.abort();
    const controller = new AbortController();
    abortControllerRef.current = controller;

    setStatus("loading");
    setErrorMessage(null);

    getConsolidatedVectorLayers(projectId, imageId, paletteId, controller.signal)
      .then((response) => {
        abortControllerRef.current = null;
        setLayersById(toLayersById(response.layers));
        setStatus("ready");
      })
      .catch((error: unknown) => {
        abortControllerRef.current = null;
        if (error instanceof ApiClientError && error.isAborted) {
          return;
        }
        setStatus("error");
        setErrorMessage(GENERIC_ERROR_MESSAGE);
      });
  }, [projectId, imageId, paletteId, layerSetId]);

  return { status, layersById, errorMessage, refetch };
}
