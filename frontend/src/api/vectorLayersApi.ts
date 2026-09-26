import { API_BASE_URL, httpClient } from "./httpClient";
import type { VectorLayerSetResponse } from "../types/vectorLayers";

function baseUrl(projectId: string, imageId: string, paletteId: string): string {
  return `/api/v1/projects/${projectId}/images/${imageId}/color-palette/${paletteId}/layers`;
}

/**
 * Genera (u obtiene, si ya existe para esa paleta+versión confirmada) el
 * conjunto COMPLETO de capas vectoriales de una paleta de colores YA
 * confirmada -- una capa por color, en una sola llamada. Sin body: no hay
 * parámetros ajustables en este sprint (mismo criterio que
 * Vectify.Api.Vectorization.VectorParameters).
 */
export function generateVectorLayers(
  projectId: string,
  imageId: string,
  paletteId: string,
  signal?: AbortSignal,
): Promise<VectorLayerSetResponse> {
  return httpClient.postJson<VectorLayerSetResponse>(baseUrl(projectId, imageId, paletteId), {}, { signal });
}

/** URL del SVG ya generado de una capa individual -- reutiliza el endpoint de vectores de M1-S05. */
export function getVectorLayerSvgUrl(projectId: string, imageId: string, vectorId: string): string {
  return `${API_BASE_URL}/api/v1/projects/${projectId}/images/${imageId}/vectors/${vectorId}`;
}
