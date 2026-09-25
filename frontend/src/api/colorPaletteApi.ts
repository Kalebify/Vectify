import { API_BASE_URL, httpClient } from "./httpClient";
import type { ColorPaletteResponse } from "../types/colorPalette";

function baseUrl(projectId: string, imageId: string): string {
  return `/api/v1/projects/${projectId}/images/${imageId}/color-palette`;
}

/**
 * Detecta la paleta de colores de la imagen original ya subida. Sin
 * `paletteId`: arranca una sesión nueva. Con `paletteId`: re-detecta bajo
 * esa misma sesión (mismos parámetros -> cache-hit del lado de la Web API).
 */
export function detectColorPalette(
  projectId: string,
  imageId: string,
  params: { paletteId?: string | null; tolerance?: number | null; maxColors?: number | null },
  signal?: AbortSignal,
): Promise<ColorPaletteResponse> {
  return httpClient.postJson<ColorPaletteResponse>(
    `${baseUrl(projectId, imageId)}/detect`,
    { paletteId: params.paletteId ?? null, tolerance: params.tolerance ?? null, maxColors: params.maxColors ?? null },
    { signal },
  );
}

/** Fusiona 2+ grupos de color en uno solo (crea una nueva versión de la paleta). */
export function mergeColorPaletteGroups(
  projectId: string,
  imageId: string,
  paletteId: string,
  groupIds: string[],
  name: string | null,
  signal?: AbortSignal,
): Promise<ColorPaletteResponse> {
  return httpClient.postJson<ColorPaletteResponse>(
    `${baseUrl(projectId, imageId)}/${paletteId}/merge`,
    { groupIds, name },
    { signal },
  );
}

/** Deshace el último merge que produjo el grupo indicado (mientras la paleta no esté confirmada). */
export function unmergeColorPaletteGroup(
  projectId: string,
  imageId: string,
  paletteId: string,
  groupId: string,
  signal?: AbortSignal,
): Promise<ColorPaletteResponse> {
  return httpClient.postJson<ColorPaletteResponse>(
    `${baseUrl(projectId, imageId)}/${paletteId}/unmerge`,
    { groupId },
    { signal },
  );
}

/** Renombra un grupo de color (crea una nueva versión de la paleta). */
export function renameColorPaletteGroup(
  projectId: string,
  imageId: string,
  paletteId: string,
  groupId: string,
  name: string,
  signal?: AbortSignal,
): Promise<ColorPaletteResponse> {
  return httpClient.postJson<ColorPaletteResponse>(
    `${baseUrl(projectId, imageId)}/${paletteId}/rename`,
    { groupId, name },
    { signal },
  );
}

/** Confirma la paleta final: queda como entrada declarada de M2-S02. */
export function confirmColorPalette(
  projectId: string,
  imageId: string,
  paletteId: string,
  signal?: AbortSignal,
): Promise<ColorPaletteResponse> {
  return httpClient.postJson<ColorPaletteResponse>(`${baseUrl(projectId, imageId)}/${paletteId}/confirm`, {}, { signal });
}

/** URL para mostrar (en un <img>) el preview cuantizado en vivo de la última versión de una sesión. */
export function getColorPalettePreviewUrl(projectId: string, imageId: string, paletteId: string): string {
  return `${API_BASE_URL}${baseUrl(projectId, imageId)}/${paletteId}/preview`;
}
