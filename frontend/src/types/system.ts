/**
 * Contrato tipado que expone ASP.NET Core en GET /api/v1/system/health.
 * Debe reflejar exactamente Vectify.Api.Contracts.SystemHealthResponse del backend.
 */

export type PythonStatus =
  | "online"
  | "unavailable"
  | "timeout"
  | "invalid_response"
  | "error";

export type SystemStatus = "online" | "degraded";

export interface ApiHealthInfo {
  status: "online";
}

export interface PythonHealthInfo {
  status: PythonStatus;
  service: string | null;
  version: string | null;
  message: string | null;
}

export interface SystemHealthResponse {
  status: SystemStatus;
  timestamp: string;
  api: ApiHealthInfo;
  python: PythonHealthInfo;
}

/**
 * Estado de la pantalla de diagnóstico en el frontend. "error" es un estado
 * puramente de cliente: significa que ni siquiera se pudo contactar a la Web API
 * (a diferencia de "degraded", que es un estado que la propia Web API reporta).
 */
export type DiagnosticsStatus = "loading" | "online" | "degraded" | "error";
