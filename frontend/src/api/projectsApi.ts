import { API_BASE_URL, uploadFile } from "./httpClient";
import type { UploadImageResponse } from "../types/upload";

const IDEMPOTENCY_HEADER = "Idempotency-Key";

export interface UploadProjectImageOptions {
  onProgress?: (percent: number) => void;
  signal?: AbortSignal;
  /**
   * Evita crear un proyecto duplicado si la misma carga se reintenta (doble click,
   * reintento tras un error de red que sí llegó a completarse en el servidor).
   * La Web API devuelve el mismo proyecto en vez de crear uno nuevo.
   */
  idempotencyKey?: string;
}

export function uploadProjectImage(
  file: File,
  options?: UploadProjectImageOptions,
): Promise<UploadImageResponse> {
  return uploadFile<UploadImageResponse>("/api/v1/projects", file, {
    onProgress: options?.onProgress,
    signal: options?.signal,
    headers: options?.idempotencyKey ? { [IDEMPOTENCY_HEADER]: options.idempotencyKey } : undefined,
  });
}

/** URL para recuperar (y previsualizar) el original ya cargado, sin procesarlo. */
export function getOriginalImageUrl(projectId: string, imageId: string): string {
  return `${API_BASE_URL}/api/v1/projects/${projectId}/images/${imageId}/original`;
}
