import { useCallback, useEffect, useRef, useState } from "react";
import { applySimplification, previewSimplification } from "../api/simplifyApi";
import { ApiClientError } from "../api/httpClient";
import type { ApiErrorResponse } from "../types/preprocess";
import type { SimplifyErrorCode, SimplifyPreset, SimplifyPreviewResponse, SimplifyResponse } from "../types/simplify";

/**
 * Estados explícitos pedidos por spec.md M1-S07: "elegir tolerancia/preset,
 * ver preview y contador antes/después, aplicar o cancelar". A diferencia de
 * useThreshold/usePreprocess (que piden automáticamente con debounce), acá el
 * preview se dispara a pedido -- mismo criterio que useVectorize (M1-S05):
 * no hay razón de negocio para llamar a Python en cada cambio de preset antes
 * de que el usuario confirme cuál quiere ver.
 */
export type SimplifyStatus = "idle" | "previewing" | "preview-ready" | "applying" | "applied" | "error";

const DEFAULT_PRESET: SimplifyPreset = "medium";

const GENERIC_ERROR_MESSAGE = "No se pudo generar la simplificación. Intentá de nuevo.";

const KNOWN_ERROR_CODES: SimplifyErrorCode[] = [
  "invalid_parameters",
  "not_found",
  "invalid_input_svg",
  "svg_too_large",
  "timeout",
  "engine_unavailable",
  "invalid_response",
  "processing_error",
  "storage_failure",
  "internal_error",
  "network_error",
];

function isKnownErrorCode(code: string): code is SimplifyErrorCode {
  return (KNOWN_ERROR_CODES as string[]).includes(code);
}

export interface UseSimplifyState {
  preset: SimplifyPreset;
  status: SimplifyStatus;
  /** Preview vigente (SIN persistir) -- se limpia al cancelar o al aplicar con éxito. */
  preview: SimplifyPreviewResponse | null;
  /** Simplificación YA aplicada (persistida como nueva versión) en esta sesión del panel. */
  applied: SimplifyResponse | null;
  errorCode: SimplifyErrorCode | null;
  errorMessage: string | null;
  setPreset: (preset: SimplifyPreset) => void;
  /** Pide un preview para el preset elegido (no persiste nada). */
  requestPreview: () => void;
  /** Aplica (persiste) la simplificación ya previsualizada. No-op si todavía no hay preview. */
  apply: () => void;
  /** Descarta el preview vigente sin llamar a la Web API -- nunca se persistió nada que deshacer. */
  cancel: () => void;
}

/**
 * Orquesta el panel de simplificación de nodos descrito en spec.md M1-S07:
 * selector de preset, preview reversible con métricas antes/después, y
 * aplicar/cancelar. Opera sobre un vectorId YA vectorizado (M1-S05): etapa
 * posterior del mismo pipeline.
 */
export function useSimplify(
  projectId: string,
  imageId: string,
  sourceVectorId: string | null,
): UseSimplifyState {
  const [preset, setPresetState] = useState<SimplifyPreset>(DEFAULT_PRESET);
  const [status, setStatus] = useState<SimplifyStatus>("idle");
  const [preview, setPreview] = useState<SimplifyPreviewResponse | null>(null);
  const [applied, setApplied] = useState<SimplifyResponse | null>(null);
  const [errorCode, setErrorCode] = useState<SimplifyErrorCode | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const abortControllerRef = useRef<AbortController | null>(null);

  useEffect(() => {
    return () => abortControllerRef.current?.abort();
  }, []);

  const handleError = useCallback((error: unknown) => {
    if (error instanceof ApiClientError && error.isAborted) {
      return false;
    }

    if (error instanceof ApiClientError && error.body) {
      const body = error.body as Partial<ApiErrorResponse>;
      const code = typeof body.code === "string" && isKnownErrorCode(body.code) ? body.code : "internal_error";
      setErrorCode(code);
      setErrorMessage(body.message ?? GENERIC_ERROR_MESSAGE);
    } else if (error instanceof ApiClientError && error.isNetworkError) {
      setErrorCode("network_error");
      setErrorMessage("No se pudo contactar a la Web API para simplificar el vector.");
    } else {
      setErrorCode("internal_error");
      setErrorMessage(GENERIC_ERROR_MESSAGE);
    }
    setStatus("error");
    return true;
  }, []);

  const requestPreview = useCallback(() => {
    if (!sourceVectorId) {
      return;
    }

    abortControllerRef.current?.abort();
    const controller = new AbortController();
    abortControllerRef.current = controller;

    setStatus("previewing");
    setErrorCode(null);
    setErrorMessage(null);
    setApplied(null);

    previewSimplification(projectId, imageId, sourceVectorId, preset, controller.signal)
      .then((response) => {
        abortControllerRef.current = null;
        setPreview(response);
        setStatus("preview-ready");
      })
      .catch((error: unknown) => {
        abortControllerRef.current = null;
        handleError(error);
      });
  }, [projectId, imageId, sourceVectorId, preset, handleError]);

  const apply = useCallback(() => {
    if (!sourceVectorId || !preview) {
      return;
    }

    abortControllerRef.current?.abort();
    const controller = new AbortController();
    abortControllerRef.current = controller;

    setStatus("applying");
    setErrorCode(null);
    setErrorMessage(null);

    applySimplification(projectId, imageId, sourceVectorId, preset, controller.signal)
      .then((response) => {
        abortControllerRef.current = null;
        setApplied(response);
        setPreview(null);
        setStatus("applied");
      })
      .catch((error: unknown) => {
        abortControllerRef.current = null;
        // Si falla aplicar, el preview sigue vigente (nunca se persistió
        // nada): el usuario puede reintentar aplicar o cancelar.
        handleError(error);
      });
  }, [projectId, imageId, sourceVectorId, preset, preview, handleError]);

  const cancel = useCallback(() => {
    // Reversible por construcción: PreviewAsync nunca persistió nada del
    // lado de la Web API, así que "cancelar" acá es puramente descartar
    // estado local de React -- sin llamada de red.
    abortControllerRef.current?.abort();
    abortControllerRef.current = null;
    setPreview(null);
    setStatus("idle");
    setErrorCode(null);
    setErrorMessage(null);
  }, []);

  const setPreset = useCallback((next: SimplifyPreset) => {
    setPresetState(next);
    // Cambiar el preset invalida cualquier preview vigente (ya no
    // corresponde al preset seleccionado) -- el usuario tiene que volver a
    // pedir preview explícitamente, mismo criterio de disparo manual que el
    // resto del hook.
    setPreview(null);
    setStatus("idle");
    setErrorCode(null);
    setErrorMessage(null);
  }, []);

  return {
    preset,
    status,
    preview,
    applied,
    errorCode,
    errorMessage,
    setPreset,
    requestPreview,
    apply,
    cancel,
  };
}
