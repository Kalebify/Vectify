/**
 * Contratos tipados que expone ASP.NET Core para la etapa de simplificación de
 * nodos (POST .../simplify/preview, POST .../simplify/apply, GET
 * .../simplifications/{id}). Deben reflejar exactamente
 * Vectify.Api.Contracts.SimplifyPreviewResponse/SimplifyResponse/SimplifyRequest.
 */

import type { VectorMetricsPayload } from "./vectorize";

/** Presets Bajo/Medio/Alto que ve el usuario -- ver Vectify.Api.Options.SimplificationOptions. */
export type SimplifyPreset = "low" | "medium" | "high";

export interface SimplificationMetricsPayload {
  before: VectorMetricsPayload;
  after: VectorMetricsPayload;
  reductionPercent: number;
}

/** Respuesta de POST .../simplify/preview: nunca persiste nada (ver spec.md M1-S07). */
export interface SimplifyPreviewResponse {
  projectId: string;
  imageId: string;
  sourceVectorId: string;
  svg: string;
  width: number;
  height: number;
  metrics: SimplificationMetricsPayload;
  preset: SimplifyPreset | null;
  tolerance: number;
}

/** Respuesta de POST .../simplify/apply y GET .../simplifications/{id}. */
export interface SimplifyResponse {
  projectId: string;
  imageId: string;
  simplificationId: string;
  svgUrl: string;
  sourceVectorId: string;
  version: number;
  width: number;
  height: number;
  metrics: SimplificationMetricsPayload;
  preset: SimplifyPreset | null;
  tolerance: number;
  cached: boolean;
}

/**
 * Code es estable y se mapea a copy en React sin parsear message. Valores que
 * pueden devolver POST .../simplify/preview y POST .../simplify/apply.
 */
export type SimplifyErrorCode =
  | "invalid_parameters"
  | "not_found"
  | "invalid_input_svg"
  | "svg_too_large"
  | "timeout"
  | "engine_unavailable"
  | "invalid_response"
  | "processing_error"
  | "storage_failure"
  | "internal_error"
  | "network_error";
