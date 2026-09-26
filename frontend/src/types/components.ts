/**
 * Contratos tipados que expone ASP.NET Core para el análisis de componentes
 * físicos independientes por capa (M2-S03): POST/GET
 * .../vectors/{vectorId}/components. Deben reflejar exactamente
 * Vectify.Api.Contracts.ComponentSetResponse/LayerComponentPayload.
 */

export interface ComponentBoundsPayload {
  minX: number;
  minY: number;
  maxX: number;
  maxY: number;
}

/** "hole" = agujero interno (resta al área neta); pertenece al MISMO componente que lo contiene, nunca es uno aparte. */
export type ComponentMemberRole = "solid" | "hole";

export interface ComponentMemberPayload {
  pathIndex: number;
  subpathIndex: number;
  role: ComponentMemberRole;
  bounds: ComponentBoundsPayload;
  area: number;
}

/** Un componente físico independiente -- un conjunto de subpaths que forman una única pieza física conexa. */
export interface LayerComponentPayload {
  id: string;
  members: ComponentMemberPayload[];
  bounds: ComponentBoundsPayload;
  area: number;
  /** True si el área neta está por debajo del umbral de "diminuto" -- se reporta igual, nunca se filtra. */
  isTiny: boolean;
}

/** Respuesta de POST/GET .../vectors/{vectorId}/components: siempre el último análisis vigente para esa capa. */
export interface ComponentSetResponse {
  projectId: string;
  imageId: string;
  componentSetId: string;
  version: number;
  vectorId: string;
  components: LayerComponentPayload[];
  skippedPathCount: number;
  cached: boolean;
}

/**
 * Code es estable y se mapea a copy en React sin parsear message. Valores
 * que pueden devolver los endpoints de componentes.
 */
export type ComponentErrorCode =
  | "not_found"
  | "invalid_input_svg"
  | "svg_too_large"
  | "too_many_subpaths"
  | "invalid_parameters"
  | "timeout"
  | "engine_unavailable"
  | "invalid_response"
  | "processing_error"
  | "storage_failure"
  | "internal_error"
  | "network_error";
