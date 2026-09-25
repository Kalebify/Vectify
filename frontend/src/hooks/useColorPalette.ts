import { useCallback, useEffect, useRef, useState } from "react";
import {
  confirmColorPalette,
  detectColorPalette,
  mergeColorPaletteGroups,
  renameColorPaletteGroup,
  unmergeColorPaletteGroup,
} from "../api/colorPaletteApi";
import { ApiClientError } from "../api/httpClient";
import type { ApiErrorResponse } from "../types/preprocess";
import type { ColorPaletteErrorCode, ColorPaletteResponse } from "../types/colorPalette";

/**
 * Estados explícitos pedidos por spec.md M2-S01: "ver paleta detectada,
 * número de colores, fusionar colores parecidos, renombrar grupos y
 * confirmar paleta", con el preview cuantizado actualizado en vivo tras cada
 * cambio (ver `palette.previewUrl` + `version`, que el panel usa para
 * invalidar la caché de la imagen).
 */
export type ColorPaletteStatus = "idle" | "detecting" | "ready" | "mutating" | "confirmed" | "error";

const GENERIC_ERROR_MESSAGE = "No se pudo procesar la paleta de colores. Intentá de nuevo.";

const KNOWN_ERROR_CODES: ColorPaletteErrorCode[] = [
  "invalid_parameters",
  "not_found",
  "group_not_found",
  "palette_confirmed",
  "not_merged",
  "empty_palette",
  "corrupt_image",
  "dimensions_exceeded",
  "timeout",
  "engine_unavailable",
  "invalid_response",
  "processing_error",
  "storage_failure",
  "internal_error",
  "network_error",
];

function isKnownErrorCode(code: string): code is ColorPaletteErrorCode {
  return (KNOWN_ERROR_CODES as string[]).includes(code);
}

export interface UseColorPaletteState {
  status: ColorPaletteStatus;
  palette: ColorPaletteResponse | null;
  tolerance: number;
  maxColors: number | null;
  /** GroupId de los grupos actualmente seleccionados para fusionar (selección múltiple). */
  selectedGroupIds: string[];
  errorCode: ColorPaletteErrorCode | null;
  errorMessage: string | null;
  setTolerance: (tolerance: number) => void;
  setMaxColors: (maxColors: number | null) => void;
  /** Detecta (o re-detecta, si ya hay una sesión abierta) con la tolerancia/maxColors vigentes. */
  detect: () => void;
  toggleGroupSelection: (groupId: string) => void;
  clearSelection: () => void;
  /** Fusiona los grupos actualmente seleccionados (no-op si hay menos de 2). */
  mergeSelected: (name?: string) => void;
  unmerge: (groupId: string) => void;
  rename: (groupId: string, name: string) => void;
  confirm: () => void;
}

export function useColorPalette(projectId: string, imageId: string): UseColorPaletteState {
  const [status, setStatus] = useState<ColorPaletteStatus>("idle");
  const [palette, setPalette] = useState<ColorPaletteResponse | null>(null);
  const [tolerance, setTolerance] = useState(12);
  const [maxColors, setMaxColors] = useState<number | null>(null);
  const [selectedGroupIds, setSelectedGroupIds] = useState<string[]>([]);
  const [errorCode, setErrorCode] = useState<ColorPaletteErrorCode | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const abortControllerRef = useRef<AbortController | null>(null);

  useEffect(() => {
    return () => abortControllerRef.current?.abort();
  }, []);

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
      setErrorMessage("No se pudo contactar a la Web API para procesar la paleta de colores.");
    } else {
      setErrorCode("internal_error");
      setErrorMessage(GENERIC_ERROR_MESSAGE);
    }
    setStatus("error");
  }, []);

  const applyPalette = useCallback((response: ColorPaletteResponse) => {
    setPalette(response);
    setSelectedGroupIds([]);
    setStatus(response.isConfirmed ? "confirmed" : "ready");
    setErrorCode(null);
    setErrorMessage(null);
  }, []);

  const runRequest = useCallback(
    (nextStatus: ColorPaletteStatus, request: (signal: AbortSignal) => Promise<ColorPaletteResponse>) => {
      abortControllerRef.current?.abort();
      const controller = new AbortController();
      abortControllerRef.current = controller;

      setStatus(nextStatus);
      setErrorCode(null);
      setErrorMessage(null);

      request(controller.signal)
        .then((response) => {
          abortControllerRef.current = null;
          applyPalette(response);
        })
        .catch((error: unknown) => {
          abortControllerRef.current = null;
          handleError(error);
        });
    },
    [applyPalette, handleError],
  );

  const detect = useCallback(() => {
    runRequest("detecting", (signal) =>
      detectColorPalette(
        projectId,
        imageId,
        { paletteId: palette && !palette.isConfirmed ? palette.paletteId : null, tolerance, maxColors },
        signal,
      ),
    );
  }, [runRequest, projectId, imageId, palette, tolerance, maxColors]);

  const toggleGroupSelection = useCallback((groupId: string) => {
    setSelectedGroupIds((current) =>
      current.includes(groupId) ? current.filter((id) => id !== groupId) : [...current, groupId],
    );
  }, []);

  const clearSelection = useCallback(() => setSelectedGroupIds([]), []);

  const mergeSelected = useCallback(
    (name?: string) => {
      if (!palette || selectedGroupIds.length < 2) {
        return;
      }

      runRequest("mutating", (signal) =>
        mergeColorPaletteGroups(projectId, imageId, palette.paletteId, selectedGroupIds, name ?? null, signal),
      );
    },
    [runRequest, projectId, imageId, palette, selectedGroupIds],
  );

  const unmerge = useCallback(
    (groupId: string) => {
      if (!palette) {
        return;
      }

      runRequest("mutating", (signal) => unmergeColorPaletteGroup(projectId, imageId, palette.paletteId, groupId, signal));
    },
    [runRequest, projectId, imageId, palette],
  );

  const rename = useCallback(
    (groupId: string, name: string) => {
      if (!palette) {
        return;
      }

      runRequest("mutating", (signal) =>
        renameColorPaletteGroup(projectId, imageId, palette.paletteId, groupId, name, signal),
      );
    },
    [runRequest, projectId, imageId, palette],
  );

  const confirm = useCallback(() => {
    if (!palette) {
      return;
    }

    runRequest("mutating", (signal) => confirmColorPalette(projectId, imageId, palette.paletteId, signal));
  }, [runRequest, projectId, imageId, palette]);

  return {
    status,
    palette,
    tolerance,
    maxColors,
    selectedGroupIds,
    errorCode,
    errorMessage,
    setTolerance,
    setMaxColors,
    detect,
    toggleGroupSelection,
    clearSelection,
    mergeSelected,
    unmerge,
    rename,
    confirm,
  };
}
