/**
 * Contratos tipados que expone ASP.NET Core para el conjunto de capas
 * vectoriales por color (M2-S02): POST/GET
 * /api/v1/projects/{id}/images/{id}/color-palette/{paletteId}/layers. Deben
 * reflejar exactamente Vectify.Api.Contracts.VectorLayerSetResponse/VectorLayerPayload.
 */

export interface VectorLayerPayload {
  groupId: string;
  name: string;
  colorHex: string;
  areaPercent: number;
  hasPartialAlpha: boolean;
  vectorId: string;
  /** URL del SVG ya generado de esta capa -- reutiliza el endpoint de vectores de M1-S05. */
  svgUrl: string;
}

/** Respuesta de generar/obtener el conjunto de capas: siempre la última versión vigente de la sesión de paleta. */
export interface VectorLayerSetResponse {
  projectId: string;
  imageId: string;
  layerSetId: string;
  version: number;
  paletteId: string;
  paletteVersion: number;
  sourceWidthPx: number;
  sourceHeightPx: number;
  layers: VectorLayerPayload[];
  cached: boolean;
}

/**
 * Code es estable y se mapea a copy en React sin parsear message. Valores que
 * pueden devolver los endpoints del conjunto de capas.
 */
export type VectorLayerErrorCode =
  | "not_found"
  | "palette_not_confirmed"
  | "corrupt_file"
  | "dimensions_exceeded"
  | "empty_mask"
  | "invalid_parameters"
  | "invalid_response"
  | "timeout"
  | "engine_unavailable"
  | "processing_error"
  | "storage_failure"
  | "internal_error"
  | "network_error";
