import { httpClient } from "./httpClient";
import type { ManufacturingOperationChoice, ManufacturingOperationSetResponse } from "../types/manufacturingOperations";

function baseUrl(projectId: string, imageId: string, paletteId: string): string {
  return `/api/v1/projects/${projectId}/images/${imageId}/color-palette/${paletteId}/layers`;
}

/** Recupera, con leyenda y resumen, la operación de fabricación vigente de TODAS las capas del conjunto ACTUAL de una paleta. */
export function getManufacturingOperations(
  projectId: string,
  imageId: string,
  paletteId: string,
  signal?: AbortSignal,
): Promise<ManufacturingOperationSetResponse> {
  return httpClient.get<ManufacturingOperationSetResponse>(`${baseUrl(projectId, imageId, paletteId)}/operations`, { signal });
}

/** Asigna (o reasigna) la intención de fabricación de una capa. NUNCA modifica geometría ni crea una nueva VectorLayerSetVersion. */
export function assignManufacturingOperation(
  projectId: string,
  imageId: string,
  paletteId: string,
  groupId: string,
  operation: ManufacturingOperationChoice,
  signal?: AbortSignal,
): Promise<ManufacturingOperationSetResponse> {
  return httpClient.postJson<ManufacturingOperationSetResponse>(
    `${baseUrl(projectId, imageId, paletteId)}/${groupId}/operation`,
    { operation },
    { signal },
  );
}
