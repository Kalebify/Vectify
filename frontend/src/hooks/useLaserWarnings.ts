import { useCallback, useRef, useState } from "react";
import { runPathCheck } from "../api/checkApi";
import { ApiClientError } from "../api/httpClient";
import type { CheckResponse } from "../types/check";

export type LaserWarningsStatus = "idle" | "running" | "ready" | "error";

interface LaserWarningsEntry {
  status: LaserWarningsStatus;
  result: CheckResponse | null;
  errorMessage: string | null;
}

export interface UseLaserWarningsState {
  statusFor: (groupId: string) => LaserWarningsStatus;
  resultFor: (groupId: string) => CheckResponse | null;
  errorFor: (groupId: string) => string | null;
  /** Dispara el Laser Checker (M1-S08) para esa capa -- SIEMPRE a pedido del usuario, nunca automático. */
  run: (groupId: string, vectorId: string) => void;
}

const IDLE_ENTRY: LaserWarningsEntry = { status: "idle", result: null, errorMessage: null };

/**
 * Cache de SESIÓN (por groupId) de resultados del Laser Checker (M1-S08)
 * para el Inspector del Workspace (M2.1-S07, "warnings láser disponibles").
 *
 * Decisión documentada (ver spec.md, "Ambigüedades detectadas" -- "Warnings
 * láser en el Inspector"): `Vectorify.Api.Checking.CheckService` es, por
 * diseño, de SOLO LECTURA y no persiste absolutamente nada en el backend
 * (ver su propio docstring: "sin caché ni versionado, cada llamada vuelve a
 * analizar el SVG desde cero") -- así que "el último resultado YA
 * disponible" solo puede vivir del lado del cliente, y solo mientras dure
 * esta sesión del Workspace. Este hook NUNCA dispara un análisis
 * automáticamente (ni al cargar el documento, ni al cambiar de capa
 * seleccionada): el Inspector debe mostrar un estado vacío honesto hasta que
 * el usuario pulse explícitamente "Ejecutar Laser Checker" (`run`) -- desde
 * ahí el resultado queda cacheado para esa capa (cambiar de capa y volver no
 * lo pierde, pero cerrar/recargar el Workspace sí).
 */
export function useLaserWarnings(projectId: string, imageId: string): UseLaserWarningsState {
  const [entriesByGroupId, setEntriesByGroupId] = useState<Record<string, LaserWarningsEntry>>({});
  const abortControllersRef = useRef<Map<string, AbortController>>(new Map());

  const run = useCallback(
    (groupId: string, vectorId: string) => {
      abortControllersRef.current.get(groupId)?.abort();
      const controller = new AbortController();
      abortControllersRef.current.set(groupId, controller);

      setEntriesByGroupId((current) => ({ ...current, [groupId]: { status: "running", result: null, errorMessage: null } }));

      runPathCheck(projectId, imageId, "vector", vectorId, controller.signal)
        .then((response) => {
          setEntriesByGroupId((current) => ({ ...current, [groupId]: { status: "ready", result: response, errorMessage: null } }));
        })
        .catch((error: unknown) => {
          if (error instanceof ApiClientError && error.isAborted) {
            return;
          }
          const message =
            error instanceof ApiClientError && error.body
              ? ((error.body as { message?: string }).message ?? "No se pudo analizar el SVG.")
              : "No se pudo analizar el SVG.";
          setEntriesByGroupId((current) => ({ ...current, [groupId]: { status: "error", result: null, errorMessage: message } }));
        });
    },
    [projectId, imageId],
  );

  const statusFor = useCallback((groupId: string) => (entriesByGroupId[groupId] ?? IDLE_ENTRY).status, [entriesByGroupId]);
  const resultFor = useCallback((groupId: string) => (entriesByGroupId[groupId] ?? IDLE_ENTRY).result, [entriesByGroupId]);
  const errorFor = useCallback((groupId: string) => (entriesByGroupId[groupId] ?? IDLE_ENTRY).errorMessage, [entriesByGroupId]);

  return { statusFor, resultFor, errorFor, run };
}
