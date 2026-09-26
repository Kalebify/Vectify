/**
 * Contratos tipados que expone ASP.NET Core para la unión física de piezas
 * (M2-S06): POST .../vectors/{vectorId}/physical-union/preview y
 * .../confirm. Deben reflejar exactamente
 * Vectify.Api.Contracts.PhysicalUnionPreviewResponse/PhysicalUnionConfirmResponse.
 * A DIFERENCIA de "Agrupar" (M2-S05, types/componentGroups.ts): esta acción
 * SÍ modifica geometría real -- por eso sus contratos viven en un archivo
 * propio, nunca se comparten tipos con componentGroups.ts aunque hoy
 * ambos empiecen con "seleccionar 2+ componentIds".
 */

export interface PhysicalUnionBoundsPayload {
  minX: number;
  minY: number;
  maxX: number;
  maxY: number;
  width: number;
  height: number;
}

export interface PhysicalUnionMetricsPayload {
  pathCount: number;
  approxNodeCount: number;
  bounds: PhysicalUnionBoundsPayload;
}

/** "boolean_union" = piezas solapadas/tangentes (sin bridge); "bridge" = piezas separadas; "mixed" = selección de 3+ piezas donde algunos pares necesitaron bridge y otros no. */
export type PhysicalUnionStrategy = "boolean_union" | "bridge" | "mixed";

/** Respuesta de POST .../physical-union/preview: geometría REAL ya calculada, SIN persistir nada. */
export interface PhysicalUnionPreviewResponse {
  projectId: string;
  imageId: string;
  vectorId: string;
  componentIds: string[];
  svg: string;
  contentType: string;
  width: number;
  height: number;
  metrics: PhysicalUnionMetricsPayload;
  componentCountBefore: number;
  componentCountAfter: number;
  strategy: PhysicalUnionStrategy;
  bridgeCount: number;
}

/** Respuesta de POST .../physical-union/confirm: la unión SÍ se persistió como una VectorVersion nueva. */
export interface PhysicalUnionConfirmResponse {
  projectId: string;
  imageId: string;
  previousVectorId: string;
  newVectorId: string;
  svgUrl: string;
  newVectorVersion: number;
  physicalUnionVersion: number;
  componentIds: string[];
  width: number;
  height: number;
  metrics: PhysicalUnionMetricsPayload;
  componentCountBefore: number;
  componentCountAfter: number;
  strategy: PhysicalUnionStrategy;
  bridgeCount: number;
}

/** Code es estable y se mapea a copy en React sin parsear message. Valores que pueden devolver los endpoints de unión física. */
export type PhysicalUnionErrorCode =
  | "not_found"
  | "component_not_found"
  | "invalid_parameters"
  | "physical_union_invalid_geometry"
  | "physical_union_impossible"
  | "invalid_input_svg"
  | "svg_too_large"
  | "timeout"
  | "engine_unavailable"
  | "invalid_response"
  | "storage_failure"
  | "internal_error"
  | "network_error";
