import { httpClient } from "./httpClient";
import type { ComponentSetResponse } from "../types/components";

function baseUrl(projectId: string, imageId: string, vectorId: string): string {
  return `/api/v1/projects/${projectId}/images/${imageId}/vectors/${vectorId}/components`;
}

/**
 * Calcula (u obtiene, si ya se calculó para ese VectorId -- inmutable una
 * vez generado) los componentes físicos independientes de UNA capa
 * vectorial ya generada (M2-S02). Sin body: no hay tolerancias ajustables
 * desde React en este sprint (Python aplica sus propios defaults).
 */
export function generateVectorComponents(
  projectId: string,
  imageId: string,
  vectorId: string,
  signal?: AbortSignal,
): Promise<ComponentSetResponse> {
  return httpClient.postJson<ComponentSetResponse>(baseUrl(projectId, imageId, vectorId), {}, { signal });
}
