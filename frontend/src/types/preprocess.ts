/**
 * Contratos tipados que expone ASP.NET Core para el preprocesamiento de imagen
 * (POST/GET /api/v1/projects/{id}/images/{id}/preview[s]/{previewId}). Deben
 * reflejar exactamente Vectify.Api.Contracts.PreprocessResponse / PreprocessRequest.
 */

export interface PreprocessParametersPayload {
  grayscale: boolean;
  contrast: number;
  brightness: number;
  denoise: number;
}

export interface PreprocessMetricsPayload {
  meanBrightness: number;
  stdDev: number;
  minValue: number;
  maxValue: number;
}

export interface PreprocessResponse {
  projectId: string;
  imageId: string;
  previewId: string;
  previewUrl: string;
  version: number;
  width: number;
  height: number;
  originalWidth: number;
  originalHeight: number;
  effectiveParams: PreprocessParametersPayload;
  metrics: PreprocessMetricsPayload;
  cached: boolean;
}

/**
 * Code es estable y se mapea a copy en React sin parsear message. Valores que
 * puede devolver POST /api/v1/projects/{id}/images/{id}/preview.
 */
export type PreprocessErrorCode =
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

export interface ApiErrorResponse {
  code: string;
  message: string;
}

/**
 * Valores por defecto y rangos de los sliders. spec.md no los cuantifica (ver
 * "Ambigüedades detectadas"); deben coincidir con Vectify.Api.Options.PreprocessOptions
 * (Preprocess:*, backend/Vectify.Api/appsettings.json) y con PreprocessParams del
 * lado Python (services/python-engine/app/models/schemas.py) — documentado como
 * supuesto en el reporte del sprint.
 */
export const PREPROCESS_DEFAULTS: PreprocessParametersPayload = {
  grayscale: false,
  contrast: 1,
  brightness: 0,
  denoise: 0,
};

export const CONTRAST_RANGE = { min: 0.5, max: 3, step: 0.1 } as const;
export const BRIGHTNESS_RANGE = { min: -100, max: 100, step: 1 } as const;
export const DENOISE_RANGE = { min: 0, max: 10, step: 1 } as const;
