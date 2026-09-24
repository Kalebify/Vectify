import { API_BASE_URL, httpClient } from "./httpClient";
import type { VectorizeResponse } from "../types/vectorize";

/**
 * Pide (o referencia, si la Web API ya generó un SVG para exactamente esta
 * máscara de origen) la vectorización de una máscara B/N ya generada
 * (M1-S04). El navegador nunca llama a Python directamente: todo pasa por la
 * Web API. Sin parámetros ajustables en este sprint (ver
 * Vectify.Api.Vectorization.VectorParameters).
 */
export function generateVector(
  projectId: string,
  imageId: string,
  sourceMaskId: string,
  signal?: AbortSignal,
): Promise<VectorizeResponse> {
  return httpClient.postJson<VectorizeResponse>(
    `/api/v1/projects/${projectId}/images/${imageId}/vectorize`,
    { maskId: sourceMaskId },
    { signal },
  );
}

/** URL para mostrar (en un <img>) los bytes de un SVG ya generado. */
export function getVectorSvgUrl(projectId: string, imageId: string, vectorId: string): string {
  return `${API_BASE_URL}/api/v1/projects/${projectId}/images/${imageId}/vectors/${vectorId}`;
}
