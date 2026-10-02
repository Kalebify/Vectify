import { httpClient } from "./httpClient";
import type {
  UpdateLayerRequestBody,
  VectorDocumentLayerResponse,
  VectorDocumentResponse,
  VectorDocumentSaveRequestBody,
  VectorDocumentSaveResponse,
} from "../types/vectorDocument";

/**
 * Guarda el VectorDocument vigente de una sesión de Workspace (M2.2-S05): crea el Project v2 en
 * el primer Save (`body.projectId` null), o agrega una DocumentVersion nueva en los siguientes
 * (`body.projectId` ya conocido). Nunca optimista del lado del cliente -- el llamador
 * (useWorkspaceSave) solo marca "saved" tras la confirmación 200/201 real de esta promesa.
 */
export function saveWorkspace(
  body: VectorDocumentSaveRequestBody,
  signal?: AbortSignal,
): Promise<VectorDocumentSaveResponse> {
  return httpClient.postJson<VectorDocumentSaveResponse>("/api/v2/workspaces/save", body, { signal });
}

/**
 * Recupera la DocumentVersion ACTUAL completa de un proyecto guardado -- usado por la
 * reapertura del Workspace vía deep-link (`savedProjectId`), en vez de reconstruir el documento
 * desde el flujo clásico (ver lib/workspaceLocation.ts).
 */
export function getVectorDocument(projectId: string, signal?: AbortSignal): Promise<VectorDocumentResponse> {
  return httpClient.get<VectorDocumentResponse>(`/api/v2/projects/${projectId}/document`, { signal });
}

/**
 * Actualiza order/visible/locked/name/operación de una capa YA guardada (cutover de los
 * sidecars clásicos tras el primer Save, M2.2-S05). Campo ausente/null = sin cambios.
 */
export function updateVectorDocumentLayer(
  projectId: string,
  layerId: string,
  patch: UpdateLayerRequestBody,
  signal?: AbortSignal,
): Promise<VectorDocumentLayerResponse> {
  return httpClient.patchJson<VectorDocumentLayerResponse>(
    `/api/v2/projects/${projectId}/layers/${layerId}`,
    patch,
    { signal },
  );
}
