import { API_BASE_URL, httpClient } from "./httpClient";
import type { ThresholdParametersPayload, ThresholdResponse } from "../types/threshold";

/**
 * Pide (o referencia, si la Web API ya generó una máscara con exactamente
 * este preview de origen y estos parámetros) una máscara de threshold. El
 * navegador nunca llama a Python directamente: todo pasa por la Web API.
 */
export function generateThresholdMask(
  projectId: string,
  imageId: string,
  sourcePreviewId: string,
  params: ThresholdParametersPayload,
  signal?: AbortSignal,
): Promise<ThresholdResponse> {
  return httpClient.postJson<ThresholdResponse>(
    `/api/v1/projects/${projectId}/images/${imageId}/threshold`,
    { previewId: sourcePreviewId, value: params.value, invert: params.invert },
    { signal },
  );
}

/** URL para mostrar (en un <img>) los bytes de una máscara ya generada. */
export function getThresholdMaskImageUrl(projectId: string, imageId: string, maskId: string): string {
  return `${API_BASE_URL}/api/v1/projects/${projectId}/images/${imageId}/masks/${maskId}`;
}
