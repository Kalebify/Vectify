/**
 * Contratos tipados que expone ASP.NET Core para la operación de fabricación
 * por color (M2-S07): POST .../color-palette/{paletteId}/layers/{groupId}/operation
 * y GET .../color-palette/{paletteId}/layers/operations. Deben reflejar
 * exactamente Vectify.Api.Contracts.ManufacturingOperationSetResponse/
 * ManufacturingOperationPayload/ManufacturingOperationSummaryPayload.
 */

/** Los 3 únicos valores que el usuario puede ELEGIR asignar. Nunca incluye "unassigned" -- ver ManufacturingOperationValue. */
export type ManufacturingOperationChoice = "cut" | "engrave" | "ignore";

/**
 * "unassigned" es un estado DERIVADO (ausencia de asignación explícita),
 * nunca un valor que el backend acepte como entrada -- ver spec.md,
 * "Ambigüedades detectadas": nunca se asume "Corte" por defecto en silencio.
 */
export type ManufacturingOperationValue = ManufacturingOperationChoice | "unassigned";

/** Una capa vista a través de su intención de fabricación -- hereda color/nombre de VectorLayerPayload (M2-S02). */
export interface ManufacturingOperationPayload {
  groupId: string;
  name: string;
  colorHex: string;
  operation: ManufacturingOperationValue;
}

/** Conteo agregado de la última versión del conjunto de asignaciones -- "N en Corte, M en Grabado, K ignoradas, J sin asignar" (ver spec.md). */
export interface ManufacturingOperationSummaryPayload {
  cutCount: number;
  engraveCount: number;
  ignoreCount: number;
  unassignedCount: number;
  totalCount: number;
}

/** Respuesta de asignar/recuperar el conjunto de operaciones: siempre la última versión vigente para el conjunto de capas ACTUAL de la paleta. */
export interface ManufacturingOperationSetResponse {
  projectId: string;
  imageId: string;
  paletteId: string;
  paletteVersion: number;
  layerSetId: string;
  version: number;
  operations: ManufacturingOperationPayload[];
  summary: ManufacturingOperationSummaryPayload;
}

/** Code es estable y se mapea a copy en React sin parsear message. Valores que pueden devolver los endpoints de operación de fabricación. */
export type ManufacturingOperationErrorCode =
  | "not_found"
  | "group_not_found"
  | "invalid_parameters"
  | "internal_error"
  | "network_error";
