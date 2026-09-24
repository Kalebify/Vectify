import { httpClient } from "./httpClient";
import type { SystemHealthResponse } from "../types/system";

export function getSystemHealth(signal?: AbortSignal): Promise<SystemHealthResponse> {
  return httpClient.get<SystemHealthResponse>("/api/v1/system/health", { signal });
}
