import { API_BASE_URL, httpClient } from "./httpClient";
import type { SimplifyPreset, SimplifyPreviewResponse, SimplifyResponse } from "../types/simplify";

/** Cuerpo JSON compartido por preview/apply: reflejan Vectify.Api.Contracts.SimplifyRequest. */
interface SimplifyRequestBody {
  vectorId: string;
  preset: SimplifyPreset | null;
  tolerance: number | null;
}

function buildRequestBody(sourceVectorId: string, preset: SimplifyPreset): SimplifyRequestBody {
  return { vectorId: sourceVectorId, preset, tolerance: null };
}

/**
 * Pide un preview de simplificación (nodeCount antes/después, % de reducción
 * y el SVG resultante) para el preset elegido, SIN persistir nada -- ver
 * spec.md M1-S07: "Preview es reversible: cancelar no deja rastro". El
 * navegador nunca llama a Python directamente: todo pasa por la Web API.
 */
export function previewSimplification(
  projectId: string,
  imageId: string,
  sourceVectorId: string,
  preset: SimplifyPreset,
  signal?: AbortSignal,
): Promise<SimplifyPreviewResponse> {
  return httpClient.postJson<SimplifyPreviewResponse>(
    `/api/v1/projects/${projectId}/images/${imageId}/simplify/preview`,
    buildRequestBody(sourceVectorId, preset),
    { signal },
  );
}

/**
 * Aplica la simplificación: persiste el resultado como una nueva versión
 * (nunca sobrescribe la anterior) -- ver spec.md M1-S07: "Aplicar crea una
 * versión nueva válida".
 */
export function applySimplification(
  projectId: string,
  imageId: string,
  sourceVectorId: string,
  preset: SimplifyPreset,
  signal?: AbortSignal,
): Promise<SimplifyResponse> {
  return httpClient.postJson<SimplifyResponse>(
    `/api/v1/projects/${projectId}/images/${imageId}/simplify/apply`,
    buildRequestBody(sourceVectorId, preset),
    { signal },
  );
}

/** URL para mostrar (en un <img>) los bytes de una simplificación ya aplicada. */
export function getSimplificationSvgUrl(projectId: string, imageId: string, simplificationId: string): string {
  return `${API_BASE_URL}/api/v1/projects/${projectId}/images/${imageId}/simplifications/${simplificationId}`;
}
