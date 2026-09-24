import { useCallback, useEffect, useRef, useState } from "react";
import { generateVector } from "../api/vectorizeApi";
import { ApiClientError } from "../api/httpClient";
import type { ApiErrorResponse } from "../types/preprocess";
import type { VectorizeErrorCode, VectorizeResponse } from "../types/vectorize";

/**
 * Estados explícitos pedidos por spec.md M1-S05, "React": "Acción de
 * vectorización, estado processing/success/error". A diferencia de
 * usePreprocess/useThreshold (que piden automáticamente al montar/mover un
 * slider, con debounce), acá el disparo es manual -- el usuario pulsa
 * "Vectorizar" ("Usuario podrá: Pulsar Vectorizar, ver progreso/estado..."):
 * no hay un valor por defecto razonable que vectorizar de entrada, y no hay
 * ningún parámetro que debounce-ear (ver VectorParameters, sin parámetros
 * ajustables en este sprint).
 */
export type VectorizeStatus = "idle" | "processing" | "success" | "error";

const GENERIC_ERROR_MESSAGE = "No se pudo vectorizar la máscara. Intentá de nuevo.";

const KNOWN_ERROR_CODES: VectorizeErrorCode[] = [
  "invalid_parameters",
  "not_found",
  "corrupt_file",
  "dimensions_exceeded",
  "empty_mask",
  "timeout",
  "engine_unavailable",
  "invalid_response",
  "processing_error",
  "storage_failure",
  "internal_error",
  "network_error",
];

function isKnownErrorCode(code: string): code is VectorizeErrorCode {
  return (KNOWN_ERROR_CODES as string[]).includes(code);
}

export interface UseVectorizeState {
  status: VectorizeStatus;
  vector: VectorizeResponse | null;
  errorCode: VectorizeErrorCode | null;
  errorMessage: string | null;
  /** Dispara (o reintenta) la vectorización de `sourceMaskId`. No-op si no hay máscara de origen todavía. */
  vectorize: () => void;
}

/**
 * Orquesta el panel de vectorización descrito en spec.md M1-S05: dispara la
 * vectorización a pedido (nunca automáticamente) sobre la máscara B/N YA
 * generada (M1-S04, etapa siguiente del mismo pipeline), y expone
 * processing/success/error. Reintentar (volver a pulsar "Vectorizar" sobre
 * la misma máscara) es idempotente del lado de la Web API -- no genera
 * vectorizaciones duplicadas.
 */
export function useVectorize(
  projectId: string,
  imageId: string,
  sourceMaskId: string | null,
): UseVectorizeState {
  const [status, setStatus] = useState<VectorizeStatus>("idle");
  const [vector, setVector] = useState<VectorizeResponse | null>(null);
  const [errorCode, setErrorCode] = useState<VectorizeErrorCode | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const abortControllerRef = useRef<AbortController | null>(null);

  // Si cambia la máscara de origen (el usuario ajustó threshold de nuevo), el
  // padre (App.tsx) remonta este panel con una `key` nueva -- mismo patrón
  // que ThresholdPanel/PreprocessPanel -- así que el estado se reinicia solo
  // (useState vuelve a sus valores iniciales) sin necesitar un efecto extra
  // que dispare setState en cascada.
  useEffect(() => {
    return () => abortControllerRef.current?.abort();
  }, []);

  const vectorize = useCallback(() => {
    if (!sourceMaskId) {
      return;
    }

    abortControllerRef.current?.abort();
    const controller = new AbortController();
    abortControllerRef.current = controller;

    setStatus("processing");
    setErrorCode(null);
    setErrorMessage(null);

    generateVector(projectId, imageId, sourceMaskId, controller.signal)
      .then((response) => {
        abortControllerRef.current = null;
        setVector(response);
        setStatus("success");
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
          setErrorMessage("No se pudo contactar a la Web API para vectorizar la máscara.");
        } else {
          setErrorCode("internal_error");
          setErrorMessage(GENERIC_ERROR_MESSAGE);
        }
        setStatus("error");
      });
  }, [projectId, imageId, sourceMaskId]);

  return { status, vector, errorCode, errorMessage, vectorize };
}
