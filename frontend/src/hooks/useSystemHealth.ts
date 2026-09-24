import { useEffect, useRef, useState } from "react";
import { getSystemHealth } from "../api/systemApi";
import { ApiClientError } from "../api/httpClient";
import type { DiagnosticsStatus, SystemHealthResponse } from "../types/system";

const POLL_INTERVAL_MS = 5000;

export interface SystemHealthState {
  status: DiagnosticsStatus;
  response: SystemHealthResponse | null;
  errorMessage: string | null;
  lastCheckedAt: Date | null;
}

/**
 * Consulta periódicamente GET /api/v1/system/health en la Web API y traduce el
 * resultado a los cuatro estados que debe representar la pantalla de
 * Diagnostics: loading, online, degraded y error.
 *
 * - "error": la Web API no respondió en absoluto (offline, red caída, etc.).
 * - "degraded"/"online": la Web API respondió y reporta su propio estado
 *   compuesto (incluye si Python está disponible o no).
 */
export function useSystemHealth(): SystemHealthState {
  const [state, setState] = useState<SystemHealthState>({
    status: "loading",
    response: null,
    errorMessage: null,
    lastCheckedAt: null,
  });

  const isFirstCheck = useRef(true);

  useEffect(() => {
    let cancelled = false;
    const controller = new AbortController();

    async function check() {
      if (isFirstCheck.current) {
        setState((prev) => ({ ...prev, status: "loading" }));
      }

      try {
        const response = await getSystemHealth(controller.signal);
        if (cancelled) return;

        setState({
          status: response.status,
          response,
          errorMessage: null,
          lastCheckedAt: new Date(),
        });
      } catch (error) {
        if (cancelled) return;

        const message =
          error instanceof ApiClientError
            ? error.message
            : "Error desconocido al consultar la Web API.";

        setState({
          status: "error",
          response: null,
          errorMessage: message,
          lastCheckedAt: new Date(),
        });
      } finally {
        isFirstCheck.current = false;
      }
    }

    check();
    const intervalId = window.setInterval(check, POLL_INTERVAL_MS);

    return () => {
      cancelled = true;
      controller.abort();
      window.clearInterval(intervalId);
    };
  }, []);

  return state;
}
