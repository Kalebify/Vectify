/**
 * Contratos tipados que expone ASP.NET Core para la etapa de vectorización
 * (POST/GET /api/v1/projects/{id}/images/{id}/vectorize|vectors/{vectorId}).
 * Deben reflejar exactamente Vectify.Api.Contracts.VectorizeResponse/VectorizeRequest.
 */

export interface VectorBoundsPayload {
  minX: number;
  minY: number;
  maxX: number;
  maxY: number;
  width: number;
  height: number;
}

export interface VectorMetricsPayload {
  pathCount: number;
  approxNodeCount: number;
  bounds: VectorBoundsPayload;
}

export interface VectorizeResponse {
  projectId: string;
  imageId: string;
  vectorId: string;
  svgUrl: string;
  sourceMaskId: string;
  version: number;
  width: number;
  height: number;
  metrics: VectorMetricsPayload;
  cached: boolean;
}

/**
 * Code es estable y se mapea a copy en React sin parsear message. Valores que
 * puede devolver POST /api/v1/projects/{id}/images/{id}/vectorize.
 */
export type VectorizeErrorCode =
  | "invalid_parameters"
  | "not_found"
  | "corrupt_file"
  | "dimensions_exceeded"
  | "empty_mask"
  | "timeout"
  | "engine_unavailable"
  | "invalid_response"
  | "processing_error"
  | "storage_failure"
  | "internal_error"
  | "network_error";
