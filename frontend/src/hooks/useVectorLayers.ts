import { useCallback, useEffect, useRef, useState } from "react";
import { generateVectorLayers } from "../api/vectorLayersApi";
import { ApiClientError } from "../api/httpClient";
import type { ApiErrorResponse } from "../types/preprocess";
import type { VectorLayerErrorCode, VectorLayerSetResponse } from "../types/vectorLayers";

/**
 * Estados explícitos pedidos por spec.md M2-S02: "ver una capa por color,
 * aislarla, ocultarla y comprobar qué geometría pertenece a ella". Mismo
 * criterio de máquina de estados que useColorPalette (M2-S01).
 */
export type VectorLayerStatus = "idle" | "generating" | "ready" | "error";

const GENERIC_ERROR_MESSAGE = "No se pudo generar el conjunto de capas. Intentá de nuevo.";

const KNOWN_ERROR_CODES: VectorLayerErrorCode[] = [
  "not_found",
  "palette_not_confirmed",
  "corrupt_file",
  "dimensions_exceeded",
  "empty_mask",
  "invalid_parameters",
  "invalid_response",
  "timeout",
  "engine_unavailable",
  "processing_error",
  "storage_failure",
  "internal_error",
  "network_error",
];

function isKnownErrorCode(code: string): code is VectorLayerErrorCode {
  return (KNOWN_ERROR_CODES as string[]).includes(code);
}

export interface UseVectorLayersState {
  status: VectorLayerStatus;
  layerSet: VectorLayerSetResponse | null;
  /** groupId -> visible. Todas las capas arrancan visibles al (re)generar el conjunto. */
  visibility: Record<string, boolean>;
  errorCode: VectorLayerErrorCode | null;
  errorMessage: string | null;
  /** Genera (o vuelve a pedir, reutilizando desde caché del lado de la Web API si la paleta no cambió) el conjunto completo de capas. */
  generate: () => void;
  toggleVisibility: (groupId: string) => void;
}

export function useVectorLayers(projectId: string, imageId: string, paletteId: string | null): UseVectorLayersState {
  const [status, setStatus] = useState<VectorLayerStatus>("idle");
  const [layerSet, setLayerSet] = useState<VectorLayerSetResponse | null>(null);
  const [visibility, setVisibility] = useState<Record<string, boolean>>({});
  const [errorCode, setErrorCode] = useState<VectorLayerErrorCode | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const abortControllerRef = useRef<AbortController | null>(null);

  useEffect(() => {
    return () => abortControllerRef.current?.abort();
  }, []);

  // Cambiar de paleta (otra sesión, u otra imagen) invalida cualquier
  // conjunto de capas ya generado: no tiene sentido seguir mostrando capas
  // de una paleta que ya no es la activa. No se resuelve con un efecto acá
  // (evita el patrón "setState síncrono dentro de un efecto" -- deriva
  // renders en cascada innecesarios): App remonta LayersPanel con una `key`
  // que incluye paletteId+version, mismo criterio que el resto de los
  // paneles del pipeline (ver App.tsx), así que un cambio de paleta ya
  // desmonta y vuelve a montar este hook con estado inicial limpio.
  const generate = useCallback(() => {
    if (!paletteId) {
      return;
    }

    abortControllerRef.current?.abort();
    const controller = new AbortController();
    abortControllerRef.current = controller;

    setStatus("generating");
    setErrorCode(null);
    setErrorMessage(null);

    generateVectorLayers(projectId, imageId, paletteId, controller.signal)
      .then((response) => {
        abortControllerRef.current = null;
        setLayerSet(response);
        setVisibility(Object.fromEntries(response.layers.map((layer) => [layer.groupId, true])));
        setStatus("ready");
      })
      .catch((error: unknown) => {
        abortControllerRef.current = null;
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
          setErrorMessage("No se pudo contactar a la Web API para generar el conjunto de capas.");
        } else {
          setErrorCode("internal_error");
          setErrorMessage(GENERIC_ERROR_MESSAGE);
        }
        setStatus("error");
      });
  }, [projectId, imageId, paletteId]);

  const toggleVisibility = useCallback((groupId: string) => {
    setVisibility((current) => ({ ...current, [groupId]: !current[groupId] }));
  }, []);

  return { status, layerSet, visibility, errorCode, errorMessage, generate, toggleVisibility };
}
