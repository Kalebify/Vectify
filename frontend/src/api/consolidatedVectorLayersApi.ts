import { httpClient } from "./httpClient";
import type { ConsolidatedVectorLayerSetResponse } from "../types/consolidatedVectorLayers";

/**
 * Recupera el conjunto de capas vigente de una paleta con el modelo
 * `VectorLayer` CONSOLIDADO (M2.1-S03/M2.1-S04): color/fill, pathCount,
 * componentCount, manufacturingOperation y validación raster-vs-vector ya
 * combinados por la Web API en una sola respuesta de solo lectura -- nunca
 * dispara ningún cálculo nuevo (ver
 * Vectorify.Api.Endpoints.ConsolidatedVectorLayerEndpoints).
 */
export function getConsolidatedVectorLayers(
  projectId: string,
  imageId: string,
  paletteId: string,
  signal?: AbortSignal,
): Promise<ConsolidatedVectorLayerSetResponse> {
  return httpClient.get<ConsolidatedVectorLayerSetResponse>(
    `/api/v1/projects/${projectId}/images/${imageId}/color-palette/${paletteId}/layers/consolidated`,
    { signal },
  );
}
