import { API_BASE_URL, httpClient } from "./httpClient";
import type { DimensionResponse, DimensionSourceKind } from "../types/dimension";

/** Cuerpo JSON de POST .../dimensions/apply: refleja Vectify.Api.Contracts.DimensionRequest. */
interface DimensionRequestBody {
  sourceKind: DimensionSourceKind;
  sourceId: string;
  widthMm: number | null;
  heightMm: number | null;
  lockAspectRatio: boolean;
}

/**
 * Aplica dimensiones físicas en mm sobre un SVG YA generado (vectorizado o
 * simplificado): persiste el resultado como una nueva versión (nunca
 * sobrescribe la anterior) -- ver spec.md M1-S09. `widthMm`/`heightMm` viajan
 * TAL COMO el usuario los completó (uno solo si la proporción está
 * bloqueada, ambos si está desbloqueada); el valor faltante en modo
 * bloqueado lo calcula ASP.NET Core a partir del aspect ratio real del SVG
 * de origen, no React -- el preview que ve el usuario ANTES de aplicar es
 * una aproximación local (ver lib/dimensionScale.ts), pero la Web API es la
 * única fuente de verdad para lo que se persiste.
 */
export function applyDimensions(
  projectId: string,
  imageId: string,
  sourceKind: DimensionSourceKind,
  sourceId: string,
  widthMm: number | null,
  heightMm: number | null,
  lockAspectRatio: boolean,
  signal?: AbortSignal,
): Promise<DimensionResponse> {
  const body: DimensionRequestBody = { sourceKind, sourceId, widthMm, heightMm, lockAspectRatio };
  return httpClient.postJson<DimensionResponse>(
    `/api/v1/projects/${projectId}/images/${imageId}/dimensions/apply`,
    body,
    { signal },
  );
}

/** URL para mostrar (en un <img> o para descargar) los bytes de un SVG con dimensiones físicas ya aplicadas. */
export function getDimensionedSvgUrl(projectId: string, imageId: string, dimensionId: string): string {
  return `${API_BASE_URL}/api/v1/projects/${projectId}/images/${imageId}/dimensions/${dimensionId}`;
}
