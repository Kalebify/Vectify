/**
 * Contratos tipados que expone ASP.NET Core para el layout interactivo de
 * capas (M2.1-S07): POST .../layers/{groupId}/visibility, POST
 * .../layers/{groupId}/lock, POST .../layers/reorder y GET .../layers/layout.
 * Deben reflejar exactamente Vectorify.Api.Contracts.LayerLayoutSetResponse/
 * LayerLayoutEntryPayload.
 */

export interface LayerLayoutEntryPayload {
  groupId: string;
  order: number;
  visible: boolean;
  locked: boolean;
  /** `null` si esa capa nunca recibió un rename explícito (ronda de fix 2) -- el nombre EFECTIVO a mostrar cae al nombre original de la capa (ver useVectorDocument.toDocument). */
  name: string | null;
}

export interface LayerLayoutSetResponse {
  projectId: string;
  imageId: string;
  paletteId: string;
  paletteVersion: number;
  layerSetId: string;
  version: number;
  entries: LayerLayoutEntryPayload[];
}

/** Code es estable y se mapea a copy en React sin parsear message. Valores que pueden devolver los endpoints de layout de capas. */
export type LayerLayoutErrorCode =
  | "not_found"
  | "group_not_found"
  | "invalid_parameters"
  | "internal_error"
  | "network_error";
