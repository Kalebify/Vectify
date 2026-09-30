import { API_BASE_URL, httpClient, uploadFile } from "./httpClient";
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

/**
 * Metadata (filename/dimensiones), sin el binario, de un proyecto/imagen ya
 * cargada (M2.1-S08): permite reconstruir `activeProject` a partir de una
 * URL (deep-link/reload del Workspace) sin volver a subir el archivo.
 * Mismo shape que `uploadProjectImage`. 404 (`not_found`) si no existe.
 */
export function getProjectImage(
  projectId: string,
  imageId: string,
  signal?: AbortSignal,
): Promise<UploadImageResponse> {
  return httpClient.get<UploadImageResponse>(`/api/v1/projects/${projectId}/images/${imageId}`, { signal });
}
