import { useCallback, useEffect, useRef, useState } from "react";
import { generatePreview } from "../api/preprocessApi";
import { ApiClientError } from "../api/httpClient";
import {
  PREPROCESS_DEFAULTS,
  type ApiErrorResponse,
  type PreprocessErrorCode,
  type PreprocessParametersPayload,
  type PreprocessResponse,
} from "../types/preprocess";

export type PreprocessStatus = "loading" | "ready" | "error";

/**
 * Tiempo de debounce entre que el usuario suelta un slider y se dispara la
 * solicitud de preview. spec.md pide debounce pero no cuantifica el tiempo;
 * 400ms es un valor razonable (evita una solicitud por cada tick del slider
 * sin sentirse lento) — documentado como supuesto en el reporte del sprint.
 */
const DEBOUNCE_MS = 400;

const GENERIC_ERROR_MESSAGE = "No se pudo generar el preview. Intentá de nuevo.";

const KNOWN_ERROR_CODES: PreprocessErrorCode[] = [
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

function isKnownErrorCode(code: string): code is PreprocessErrorCode {
  return (KNOWN_ERROR_CODES as string[]).includes(code);
}

export interface UsePreprocessState {
  /** Parámetros elegidos por el usuario (estado del proyecto, no píxeles manipulados en el navegador). */
  params: PreprocessParametersPayload;
  status: PreprocessStatus;
  /** Último preview recibido de la Web API; se mantiene visible mientras se genera uno nuevo (status "loading"). */
  preview: PreprocessResponse | null;
  errorCode: PreprocessErrorCode | null;
  errorMessage: string | null;
  isAtDefaults: boolean;
  setGrayscale: (value: boolean) => void;
  setContrast: (value: number) => void;
  setBrightness: (value: number) => void;
  setDenoise: (value: number) => void;
  reset: () => void;
}

/**
 * Orquesta el panel de preprocesamiento descrito en spec.md M1-S03: mantiene
 * los parámetros como estado de React (nunca manipula píxeles en el
 * navegador), los debounce-ea antes de pedir un preview nuevo a la Web API, y
 * expone loading/error para que la UI pueda mostrar el estado de la solicitud
 * en curso sin bloquear la interacción con los sliders.
 */
export function usePreprocess(projectId: string, imageId: string): UsePreprocessState {
  const [params, setParams] = useState<PreprocessParametersPayload>(PREPROCESS_DEFAULTS);
  const [status, setStatus] = useState<PreprocessStatus>("loading");
  const [preview, setPreview] = useState<PreprocessResponse | null>(null);
  const [errorCode, setErrorCode] = useState<PreprocessErrorCode | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const abortControllerRef = useRef<AbortController | null>(null);
  const debounceRef = useRef<number | null>(null);
  const isFirstRun = useRef(true);

  const requestPreview = useCallback(
    (nextParams: PreprocessParametersPayload) => {
      abortControllerRef.current?.abort();
      const controller = new AbortController();
      abortControllerRef.current = controller;

      setStatus("loading");
      setErrorCode(null);
      setErrorMessage(null);

      generatePreview(projectId, imageId, nextParams, controller.signal)
        .then((response) => {
          abortControllerRef.current = null;
          setPreview(response);
          setStatus("ready");
        })
        .catch((error: unknown) => {
          abortControllerRef.current = null;
          if (error instanceof ApiClientError && error.isAborted) {
            // Se canceló porque el usuario siguió moviendo un slider: la
            // próxima solicitud (debounce-eada) ya está en camino.
            return;
          }

          if (error instanceof ApiClientError && error.body) {
            const body = error.body as Partial<ApiErrorResponse>;
            const code = typeof body.code === "string" && isKnownErrorCode(body.code) ? body.code : "internal_error";
            setErrorCode(code);
            setErrorMessage(body.message ?? GENERIC_ERROR_MESSAGE);
          } else if (error instanceof ApiClientError && error.isNetworkError) {
            setErrorCode("network_error");
            setErrorMessage("No se pudo contactar a la Web API para generar el preview.");
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
    if (debounceRef.current !== null) {
      window.clearTimeout(debounceRef.current);
    }

    // La primera solicitud (valores por defecto, al montar) se dispara de
    // inmediato; los cambios posteriores de slider sí se debounce-ean.
    if (isFirstRun.current) {
      isFirstRun.current = false;
      requestPreview(params);
      return;
    }

    debounceRef.current = window.setTimeout(() => requestPreview(params), DEBOUNCE_MS);
    return () => {
      if (debounceRef.current !== null) {
        window.clearTimeout(debounceRef.current);
      }
    };
  }, [params, requestPreview]);

  useEffect(() => {
    return () => abortControllerRef.current?.abort();
  }, []);

  const updateParam = useCallback(
    <K extends keyof PreprocessParametersPayload>(key: K, value: PreprocessParametersPayload[K]) => {
      setParams((prev) => ({ ...prev, [key]: value }));
    },
    [],
  );

  const isAtDefaults =
    params.grayscale === PREPROCESS_DEFAULTS.grayscale &&
    params.contrast === PREPROCESS_DEFAULTS.contrast &&
    params.brightness === PREPROCESS_DEFAULTS.brightness &&
    params.denoise === PREPROCESS_DEFAULTS.denoise;

  return {
    params,
    status,
    preview,
    errorCode,
    errorMessage,
    isAtDefaults,
    setGrayscale: (value) => updateParam("grayscale", value),
    setContrast: (value) => updateParam("contrast", value),
    setBrightness: (value) => updateParam("brightness", value),
    setDenoise: (value) => updateParam("denoise", value),
    reset: () => setParams(PREPROCESS_DEFAULTS),
  };
}
