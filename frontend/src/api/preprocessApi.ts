import { API_BASE_URL, httpClient } from "./httpClient";
import type { PreprocessParametersPayload, PreprocessResponse } from "../types/preprocess";

/**
 * Pide (o referencia, si la Web API ya generó un preview con exactamente
 * estos parámetros) un preview preprocesado. El navegador nunca llama a
 * Python directamente: todo pasa por la Web API.
 */
export function generatePreview(
  projectId: string,
  imageId: string,
  params: PreprocessParametersPayload,
  signal?: AbortSignal,
): Promise<PreprocessResponse> {
  return httpClient.postJson<PreprocessResponse>(
    `/api/v1/projects/${projectId}/images/${imageId}/preview`,
    params,
    { signal },
  );
}

/** URL para mostrar (en un <img>) los bytes de un preview ya generado. */
export function getPreviewImageUrl(projectId: string, imageId: string, previewId: string): string {
  return `${API_BASE_URL}/api/v1/projects/${projectId}/images/${imageId}/previews/${previewId}`;
}
