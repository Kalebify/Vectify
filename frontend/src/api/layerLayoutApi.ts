import { httpClient } from "./httpClient";
import type { LayerLayoutSetResponse } from "../types/layerLayout";

function baseUrl(projectId: string, imageId: string, paletteId: string): string {
  return `/api/v1/projects/${projectId}/images/${imageId}/color-palette/${paletteId}/layers`;
}

/** Recupera el layout (order/visible/locked) vigente de TODAS las capas del conjunto ACTUAL de una paleta -- valores default para las que nunca se tocaron. */
export function getLayerLayout(
  projectId: string,
  imageId: string,
  paletteId: string,
  signal?: AbortSignal,
): Promise<LayerLayoutSetResponse> {
  return httpClient.get<LayerLayoutSetResponse>(`${baseUrl(projectId, imageId, paletteId)}/layout`, { signal });
}

/** Persiste la visibilidad (Eye) de una capa -- crea una nueva versión del layout, NUNCA muta geometría. */
export function setLayerVisible(
  projectId: string,
  imageId: string,
  paletteId: string,
  groupId: string,
  visible: boolean,
  signal?: AbortSignal,
): Promise<LayerLayoutSetResponse> {
  return httpClient.postJson<LayerLayoutSetResponse>(
    `${baseUrl(projectId, imageId, paletteId)}/${groupId}/visibility`,
    { visible },
    { signal },
  );
}

/** Persiste el bloqueo de edición (Lock) de una capa -- NO afecta su visibilidad/aislamiento/inspección. */
export function setLayerLocked(
  projectId: string,
  imageId: string,
  paletteId: string,
  groupId: string,
  locked: boolean,
  signal?: AbortSignal,
): Promise<LayerLayoutSetResponse> {
  return httpClient.postJson<LayerLayoutSetResponse>(
    `${baseUrl(projectId, imageId, paletteId)}/${groupId}/lock`,
    { locked },
    { signal },
  );
}

/** Persiste el nuevo orden visual COMPLETO de las capas (Drag & Drop) -- NUNCA toca d/transform/geometría. */
export function reorderLayers(
  projectId: string,
  imageId: string,
  paletteId: string,
  orderedGroupIds: string[],
  signal?: AbortSignal,
): Promise<LayerLayoutSetResponse> {
  return httpClient.postJson<LayerLayoutSetResponse>(
    `${baseUrl(projectId, imageId, paletteId)}/reorder`,
    { orderedGroupIds },
    { signal },
  );
}
