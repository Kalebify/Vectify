import { useCallback, useEffect, useRef, useState } from "react";
import { applyDimensions } from "../api/dimensionApi";
import { ApiClientError } from "../api/httpClient";
import { computeDimensionPreview, parseMmInput, type DimensionPreviewResult } from "../lib/dimensionScale";
import type { ApiErrorResponse } from "../types/preprocess";
import type { DimensionErrorCode, DimensionResponse, DimensionSourceKind } from "../types/dimension";

/**
 * Estados de la etapa de dimensiones físicas (M1-S09). A diferencia de
 * useSimplify, no hay un estado "previewing"/"preview-ready" separado que
 * dispare una llamada HTTP: el preview se recalcula en cada render de forma
 * puramente local (ver lib/dimensionScale.computeDimensionPreview) porque es
 * aritmética simple de escala, sin ningún motor externo de por medio -- ver
 * decisión documentada en el reporte del sprint. Solo "Aplicar" llama a la
 * Web API.
 */
export type DimensionStatus = "idle" | "applying" | "applied" | "error";

const GENERIC_ERROR_MESSAGE = "No se pudieron aplicar las dimensiones físicas. Intentá de nuevo.";

const KNOWN_ERROR_CODES: DimensionErrorCode[] = [
  "invalid_parameters",
  "dimension_out_of_range",
  "not_found",
  "storage_failure",
  "invalid_source_svg",
  "internal_error",
  "network_error",
];

function isKnownErrorCode(code: string): code is DimensionErrorCode {
  return (KNOWN_ERROR_CODES as string[]).includes(code);
}

export interface UseDimensionsState {
  widthInput: string;
  heightInput: string;
  lockAspectRatio: boolean;
  /** Preview calculado 100% en el cliente, recalculado en cada cambio -- ver lib/dimensionScale. */
  preview: DimensionPreviewResult;
  status: DimensionStatus;
  applied: DimensionResponse | null;
  errorCode: DimensionErrorCode | null;
  errorMessage: string | null;
  setWidthInput: (value: string) => void;
  setHeightInput: (value: string) => void;
  setLockAspectRatio: (locked: boolean) => void;
  /** Persiste las dimensiones ya calculadas por el preview. No-op si el preview todavía no es válido. */
  apply: () => void;
}

/**
 * Orquesta el panel de dimensiones físicas descrito en spec.md M1-S09:
 * ancho/alto en mm con proporción bloqueada (default) o desbloqueada, un
 * preview local del tamaño final, y "Aplicar" (persiste una nueva
 * DimensionVersion). Opera sobre un SVG YA generado (vectorizado o
 * simplificado, ver `sourceKind`): mismo criterio de origen dual que
 * useCheck (M1-S08).
 */
export function useDimensions(
  projectId: string,
  imageId: string,
  sourceKind: DimensionSourceKind,
  sourceId: string | null,
  sourceWidthPx: number,
  sourceHeightPx: number,
): UseDimensionsState {
  const [widthInput, setWidthInputState] = useState("");
  const [heightInput, setHeightInputState] = useState("");
  const [lockAspectRatio, setLockAspectRatioState] = useState(true);
  const [status, setStatus] = useState<DimensionStatus>("idle");
  const [applied, setApplied] = useState<DimensionResponse | null>(null);
  const [errorCode, setErrorCode] = useState<DimensionErrorCode | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const abortControllerRef = useRef<AbortController | null>(null);
  const isFirstRenderRef = useRef(true);

  useEffect(() => {
    return () => abortControllerRef.current?.abort();
  }, []);

  // Cambiar de fuente invalida cualquier resultado "aplicado" vigente (ya no
  // correspondería a lo que se está mostrando) -- mismo criterio que
  // useCheck. Los valores de ancho/alto que el usuario ya escribió se
  // conservan: siguen siendo un objetivo físico válido para la nueva fuente.
  useEffect(() => {
    if (isFirstRenderRef.current) {
      isFirstRenderRef.current = false;
      return;
    }
    abortControllerRef.current?.abort();
    abortControllerRef.current = null;
    setStatus("idle");
    setApplied(null);
    setErrorCode(null);
    setErrorMessage(null);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [sourceKind, sourceId]);

  const resetResult = useCallback(() => {
    setStatus("idle");
    setApplied(null);
    setErrorCode(null);
    setErrorMessage(null);
  }, []);

  const setWidthInput = useCallback(
    (value: string) => {
      setWidthInputState(value);
      // Con la proporción bloqueada, ancho y alto son mutuamente excluyentes
      // (ver spec.md: "usuario define ancho O alto"): completar uno limpia
      // el otro, que vuelve a calcularse. Un valor vacío no toca el otro
      // campo (permite borrar para escribir en el otro).
      if (lockAspectRatio && value.trim() !== "") {
        setHeightInputState("");
      }
      resetResult();
    },
    [lockAspectRatio, resetResult],
  );

  const setHeightInput = useCallback(
    (value: string) => {
      setHeightInputState(value);
      if (lockAspectRatio && value.trim() !== "") {
        setWidthInputState("");
      }
      resetResult();
    },
    [lockAspectRatio, resetResult],
  );

  const setLockAspectRatio = useCallback(
    (locked: boolean) => {
      // Al bloquear con ambos campos completados (posible si se completaron
      // estando desbloqueada), se conserva el ancho y se limpia el alto --
      // mismo criterio de "el ancho gana" que ResolveDimensions del lado de
      // ASP.NET Core cuando ambos vienen informados sería inválido.
      if (locked) {
        setHeightInputState((current) => (widthInput.trim() !== "" && current.trim() !== "" ? "" : current));
      }
      setLockAspectRatioState(locked);
      resetResult();
    },
    [widthInput, resetResult],
  );

  const parsedWidthMm = parseMmInput(widthInput);
  const parsedHeightMm = parseMmInput(heightInput);

  const preview = computeDimensionPreview({
    sourceWidthPx,
    sourceHeightPx,
    widthMm: parsedWidthMm,
    heightMm: parsedHeightMm,
    lockAspectRatio,
  });

  const apply = useCallback(() => {
    if (!sourceId || !preview.ok) {
      return;
    }

    abortControllerRef.current?.abort();
    const controller = new AbortController();
    abortControllerRef.current = controller;

    setStatus("applying");
    setErrorCode(null);
    setErrorMessage(null);

    applyDimensions(
      projectId,
      imageId,
      sourceKind,
      sourceId,
      parsedWidthMm,
      parsedHeightMm,
      lockAspectRatio,
      controller.signal,
    )
      .then((response) => {
        abortControllerRef.current = null;
        setApplied(response);
        setStatus("applied");
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
          setErrorMessage("No se pudo contactar a la Web API para aplicar las dimensiones físicas.");
        } else {
          setErrorCode("internal_error");
          setErrorMessage(GENERIC_ERROR_MESSAGE);
        }
        setStatus("error");
      });
  }, [projectId, imageId, sourceKind, sourceId, parsedWidthMm, parsedHeightMm, lockAspectRatio, preview.ok]);

  return {
    widthInput,
    heightInput,
    lockAspectRatio,
    preview,
    status,
    applied,
    errorCode,
    errorMessage,
    setWidthInput,
    setHeightInput,
    setLockAspectRatio,
    apply,
  };
}
