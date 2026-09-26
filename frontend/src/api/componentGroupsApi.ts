import { httpClient } from "./httpClient";
import type { ComponentGroupSetResponse } from "../types/componentGroups";

function baseUrl(projectId: string, imageId: string, vectorId: string): string {
  return `/api/v1/projects/${projectId}/images/${imageId}/vectors/${vectorId}/components/groups`;
}

/** Recupera el conjunto de grupos lógicos vigente de una capa (vacío si nunca se agrupó nada -- nunca 404). */
export function getComponentGroups(
  projectId: string,
  imageId: string,
  vectorId: string,
  signal?: AbortSignal,
): Promise<ComponentGroupSetResponse> {
  return httpClient.get<ComponentGroupSetResponse>(baseUrl(projectId, imageId, vectorId), { signal });
}

/** Agrupa 2+ componentes físicos ya calculados (M2-S03) bajo un nuevo ComponentGroup lógico. */
export function groupComponents(
  projectId: string,
  imageId: string,
  vectorId: string,
  componentIds: string[],
  name: string | null,
  signal?: AbortSignal,
): Promise<ComponentGroupSetResponse> {
  return httpClient.postJson<ComponentGroupSetResponse>(
    baseUrl(projectId, imageId, vectorId),
    { componentIds, name },
    { signal },
  );
}

/** Desagrupa: sus componentes vuelven a existir individualmente exactamente como antes de agruparlos. */
export function ungroupComponents(
  projectId: string,
  imageId: string,
  vectorId: string,
  groupId: string,
  signal?: AbortSignal,
): Promise<ComponentGroupSetResponse> {
  return httpClient.postJson<ComponentGroupSetResponse>(
    `${baseUrl(projectId, imageId, vectorId)}/${groupId}/ungroup`,
    {},
    { signal },
  );
}

/** Renombra un grupo de componentes existente. */
export function renameComponentGroup(
  projectId: string,
  imageId: string,
  vectorId: string,
  groupId: string,
  name: string,
  signal?: AbortSignal,
): Promise<ComponentGroupSetResponse> {
  return httpClient.postJson<ComponentGroupSetResponse>(
    `${baseUrl(projectId, imageId, vectorId)}/${groupId}/rename`,
    { name },
    { signal },
  );
}
