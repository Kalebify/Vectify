import { useCallback, useEffect, useRef, useState } from "react";
import { generateThresholdMask } from "../api/thresholdApi";
import { ApiClientError } from "../api/httpClient";
import type { ApiErrorResponse } from "../types/preprocess";
import {
  THRESHOLD_DEFAULTS,
  type ThresholdErrorCode,
  type ThresholdParametersPayload,
  type ThresholdResponse,
} from "../types/threshold";

export type ThresholdStatus = "loading" | "ready" | "error";

/**
 * Mismo criterio de debounce que usePreprocess (M1-S03): spec.md pide preview
 * inmediato pero no cuantifica el debounce; 400ms evita una solicitud por
 * cada tick del slider sin sentirse lento -- documentado como supuesto.
 */
const DEBOUNCE_MS = 400;

const GENERIC_ERROR_MESSAGE = "No se pudo generar la máscara. Intentá de nuevo.";

const KNOWN_ERROR_CODES: ThresholdErrorCode[] = [
  "invalid_parameters",
  "not_found",
  "corrupt_file",
  "dimensions_exceeded",
  "timeout",
  "engine_unavailable",
  "invalid_response",
  "processing_error",
  "storage_failure",
  "internal_error",
  "network_error",
];

function isKnownErrorCode(code: string): code is ThresholdErrorCode {
  return (KNOWN_ERROR_CODES as string[]).includes(code);
}

export interface UseThresholdState {
  /** Parámetros elegidos por el usuario (estado de React, nunca píxeles manipulados en el navegador). */
  params: ThresholdParametersPayload;
  status: ThresholdStatus;
  /** Última máscara recibida de la Web API; se mantiene visible mientras se genera una nueva (status "loading"). */
  mask: ThresholdResponse | null;
  errorCode: ThresholdErrorCode | null;
  errorMessage: string | null;
  isAtDefaults: boolean;
  setValue: (value: number) => void;
  setInvert: (value: boolean) => void;
  reset: () => void;
}

/**
 * Orquesta el panel de threshold B/N descrito en spec.md M1-S04: ajusta
 * umbral/inversión con debounce y pide la máscara a la Web API a partir del
 * previewId YA preprocesado (etapa siguiente del mismo pipeline, nunca opera
 * sobre el original) -- ver usePreprocess.ts (M1-S03) para el mismo patrón
 * aplicado a la etapa anterior. Expone loading/error y la advertencia de
 * máscara casi vacía/llena que calcula la Web API.
 */
export function useThreshold(
  projectId: string,
  imageId: string,
  sourcePreviewId: string | null,
): UseThresholdState {
  const [params, setParams] = useState<ThresholdParametersPayload>(THRESHOLD_DEFAULTS);
  const [status, setStatus] = useState<ThresholdStatus>("loading");
  const [mask, setMask] = useState<ThresholdResponse | null>(null);
  const [errorCode, setErrorCode] = useState<ThresholdErrorCode | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const abortControllerRef = useRef<AbortController | null>(null);
  const debounceRef = useRef<number | null>(null);
  const lastRequestedPreviewId = useRef<string | null>(null);

  const requestMask = useCallback(
    (previewId: string, nextParams: ThresholdParametersPayload) => {
      abortControllerRef.current?.abort();
      const controller = new AbortController();
      abortControllerRef.current = controller;

      setStatus("loading");
      setErrorCode(null);
      setErrorMessage(null);

      generateThresholdMask(projectId, imageId, previewId, nextParams, controller.signal)
        .then((response) => {
          abortControllerRef.current = null;
          setMask(response);
          setStatus("ready");
        })
        .catch((error: unknown) => {
          abortControllerRef.current = null;
          if (error instanceof ApiClientError && error.isAborted) {
            // Se canceló porque el usuario siguió moviendo el slider (o
            // cambió el preview de origen): la próxima solicitud ya está en camino.
            return;
          }

          if (error instanceof ApiClientError && error.body) {
            const body = error.body as Partial<ApiErrorResponse>;
            const code = typeof body.code === "string" && isKnownErrorCode(body.code) ? body.code : "internal_error";
            setErrorCode(code);
            setErrorMessage(body.message ?? GENERIC_ERROR_MESSAGE);
          } else if (error instanceof ApiClientError && error.isNetworkError) {
            setErrorCode("network_error");
            setErrorMessage("No se pudo contactar a la Web API para generar la máscara.");
          } else {
            setErrorCode("internal_error");
            setErrorMessage(GENERIC_ERROR_MESSAGE);
          }
          setStatus("error");
        });
    },
    [projectId, imageId],
  );

  useEffect(() => {
    if (!sourcePreviewId) {
      return;
    }

    if (debounceRef.current !== null) {
      window.clearTimeout(debounceRef.current);
    }

    // Primera solicitud, o cambió el preview de origen (el usuario confirmó
    // un nuevo preprocesamiento): se dispara de inmediato, sin debounce.
    if (lastRequestedPreviewId.current !== sourcePreviewId) {
      lastRequestedPreviewId.current = sourcePreviewId;
      requestMask(sourcePreviewId, params);
      return;
    }

    debounceRef.current = window.setTimeout(() => requestMask(sourcePreviewId, params), DEBOUNCE_MS);
    return () => {
      if (debounceRef.current !== null) {
        window.clearTimeout(debounceRef.current);
      }
    };
  }, [params, sourcePreviewId, requestMask]);

  useEffect(() => {
    return () => abortControllerRef.current?.abort();
  }, []);

  const updateParam = useCallback(
    <K extends keyof ThresholdParametersPayload>(key: K, value: ThresholdParametersPayload[K]) => {
      setParams((prev) => ({ ...prev, [key]: value }));
    },
    [],
  );

  const isAtDefaults = params.value === THRESHOLD_DEFAULTS.value && params.invert === THRESHOLD_DEFAULTS.invert;

  return {
    params,
    status,
    mask,
    errorCode,
    errorMessage,
    isAtDefaults,
    setValue: (value) => updateParam("value", value),
    setInvert: (value) => updateParam("invert", value),
    reset: () => setParams(THRESHOLD_DEFAULTS),
  };
}
