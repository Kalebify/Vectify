import { httpClient } from "./httpClient";
import type { PhysicalUnionConfirmResponse, PhysicalUnionPreviewResponse } from "../types/physicalUnion";

function baseUrl(projectId: string, imageId: string, vectorId: string): string {
  return `/api/v1/projects/${projectId}/images/${imageId}/vectors/${vectorId}/physical-union`;
}

/**
 * Calcula (SIN persistir nada) el resultado real de unir físicamente 2+
 * componentes seleccionados -- geometría real, no una aproximación visual.
 * Llamar este endpoint repetidamente, o nunca confirmar, no tiene ningún
 * efecto persistente (equivalente a "cancelar": simplemente no se llama a
 * `confirmPhysicalUnion`).
 */
export function previewPhysicalUnion(
  projectId: string,
  imageId: string,
  vectorId: string,
  componentIds: string[],
  signal?: AbortSignal,
): Promise<PhysicalUnionPreviewResponse> {
  return httpClient.postJson<PhysicalUnionPreviewResponse>(
    `${baseUrl(projectId, imageId, vectorId)}/preview`,
    { componentIds },
    { signal },
  );
}

/** Confirma la unión física: persiste una VectorVersion NUEVA con el SVG fusionado -- la anterior nunca se destruye. */
export function confirmPhysicalUnion(
  projectId: string,
  imageId: string,
  vectorId: string,
  componentIds: string[],
  signal?: AbortSignal,
): Promise<PhysicalUnionConfirmResponse> {
  return httpClient.postJson<PhysicalUnionConfirmResponse>(
    `${baseUrl(projectId, imageId, vectorId)}/confirm`,
    { componentIds },
    { signal },
  );
}
