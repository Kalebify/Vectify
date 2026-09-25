import { useCallback, useEffect, useRef, useState } from "react";
import { runPathCheck } from "../api/checkApi";
import { ApiClientError } from "../api/httpClient";
import type { ApiErrorResponse } from "../types/preprocess";
import type { CheckErrorCode, CheckResponse, CheckSourceKind } from "../types/check";

/**
 * Estados del Laser Checker de paths abiertos/duplicados (M1-S08): igual
 * criterio de disparo manual que useSimplify/useVectorize (M1-S07/M1-S05)
 * -- "Análisis se ejecuta a pedido del usuario (no automático en cada
 * cambio)". No hay "aplicar": el análisis es de solo lectura y nunca
 * persiste nada, así que no existe un estado equivalente a "applied".
 */
export type CheckStatus = "idle" | "running" | "ready" | "error";

const GENERIC_ERROR_MESSAGE = "No se pudo analizar el SVG. Intentá de nuevo.";

const KNOWN_ERROR_CODES: CheckErrorCode[] = [
  "invalid_parameters",
  "not_found",
  "invalid_input_svg",
  "svg_too_large",
  "too_many_subpaths",
  "timeout",
  "engine_unavailable",
  "invalid_response",
  "processing_error",
  "storage_failure",
  "internal_error",
  "network_error",
];

function isKnownErrorCode(code: string): code is CheckErrorCode {
  return (KNOWN_ERROR_CODES as string[]).includes(code);
}

export interface UseCheckState {
  status: CheckStatus;
  result: CheckResponse | null;
  errorCode: CheckErrorCode | null;
  errorMessage: string | null;
  /** Dispara el análisis a pedido del usuario (nunca automático). */
  run: () => void;
}

/**
 * Orquesta el Laser Checker de paths abiertos/duplicados descrito en
 * spec.md M1-S08: el usuario ejecuta el análisis a pedido sobre un SVG YA
 * generado (vectorizado o simplificado, ver `sourceKind`) y ve la cantidad
 * de cada tipo de issue. Cambiar de fuente (sourceKind/sourceId) invalida
 * cualquier resultado vigente -- ya no correspondería a lo que se está
 * mostrando -- así que el usuario tiene que volver a pedir el análisis
 * explícitamente, mismo criterio que useSimplify.setPreset.
 */
export function useCheck(
  projectId: string,
  imageId: string,
  sourceKind: CheckSourceKind,
  sourceId: string | null,
): UseCheckState {
  const [status, setStatus] = useState<CheckStatus>("idle");
  const [result, setResult] = useState<CheckResponse | null>(null);
  const [errorCode, setErrorCode] = useState<CheckErrorCode | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const abortControllerRef = useRef<AbortController | null>(null);
  const isFirstRenderRef = useRef(true);

  useEffect(() => {
    return () => abortControllerRef.current?.abort();
  }, []);

  // Cambiar de fuente invalida el resultado vigente (ver docstring), pero
  // no dispara un análisis nuevo automáticamente -- eso lo decide el
  // usuario pulsando "Analizar" de nuevo.
  useEffect(() => {
    if (isFirstRenderRef.current) {
      isFirstRenderRef.current = false;
      return;
    }
    abortControllerRef.current?.abort();
    abortControllerRef.current = null;
    setStatus("idle");
    setResult(null);
    setErrorCode(null);
    setErrorMessage(null);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [sourceKind, sourceId]);

  const run = useCallback(() => {
    if (!sourceId) {
      return;
    }

    abortControllerRef.current?.abort();
    const controller = new AbortController();
    abortControllerRef.current = controller;

    setStatus("running");
    setErrorCode(null);
    setErrorMessage(null);

    runPathCheck(projectId, imageId, sourceKind, sourceId, controller.signal)
      .then((response) => {
        abortControllerRef.current = null;
        setResult(response);
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
          setErrorMessage("No se pudo contactar a la Web API para analizar el SVG.");
        } else {
          setErrorCode("internal_error");
          setErrorMessage(GENERIC_ERROR_MESSAGE);
        }
        setStatus("error");
      });
  }, [projectId, imageId, sourceKind, sourceId]);

  return { status, result, errorCode, errorMessage, run };
}
