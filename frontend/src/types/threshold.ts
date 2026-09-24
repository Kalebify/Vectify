/**
 * Contratos tipados que expone ASP.NET Core para la etapa de threshold B/N
 * (POST/GET /api/v1/projects/{id}/images/{id}/threshold|masks/{maskId}).
 * Deben reflejar exactamente Vectify.Api.Contracts.ThresholdResponse/ThresholdRequest.
 */

export interface ThresholdParametersPayload {
  value: number;
  invert: boolean;
}

export interface ThresholdMetricsPayload {
  foregroundPercent: number;
  backgroundPercent: number;
  isNearEmpty: boolean;
  isNearFull: boolean;
  warningCode: string | null;
  warningMessage: string | null;
}

export interface ThresholdResponse {
  projectId: string;
  imageId: string;
  maskId: string;
  maskUrl: string;
  sourcePreviewId: string;
  version: number;
  width: number;
  height: number;
  effectiveParams: ThresholdParametersPayload;
  metrics: ThresholdMetricsPayload;
  cached: boolean;
}

/**
 * Code es estable y se mapea a copy en React sin parsear message. Valores que
 * puede devolver POST /api/v1/projects/{id}/images/{id}/threshold.
 */
export type ThresholdErrorCode =
  | "invalid_parameters"
  | "not_found"
  | "corrupt_file"
  | "dimensions_exceeded"
  | "timeout"
  | "engine_unavailable"
  | "invalid_response"
  | "processing_error"
  | "storage_failure"
  | "internal_error"
  | "network_error";

/**
 * Valor por defecto y rango del slider de umbral. spec.md no los cuantifica
 * (ver "Ambigüedades detectadas"); deben coincidir con
 * Vectify.Api.Options.ThresholdOptions (Threshold:*, backend/Vectify.Api/
 * appsettings.json) y con ThresholdParams del lado Python
 * (services/python-engine/app/models/schemas.py) -- documentado como
 * supuesto en el reporte del sprint.
 */
export const THRESHOLD_DEFAULTS: ThresholdParametersPayload = {
  value: 128,
  invert: false,
};

export const THRESHOLD_VALUE_RANGE = { min: 0, max: 255, step: 1 } as const;
