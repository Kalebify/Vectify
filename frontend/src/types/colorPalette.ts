/**
 * Contratos tipados que expone ASP.NET Core para la detección/reducción de
 * paleta de colores (M2-S01): POST .../color-palette/detect, .../merge,
 * .../unmerge, .../rename, .../confirm, GET .../color-palette/{paletteId}.
 * Deben reflejar exactamente Vectify.Api.Contracts.ColorPaletteResponse/
 * ColorGroupPayload/*Request del backend.
 */

export interface ColorGroupPayload {
  groupId: string;
  name: string;
  colorHex: string;
  pixelCount: number;
  areaPercent: number;
  hasPartialAlpha: boolean;
  maskUrl: string;
  /** true si este grupo proviene de un merge (habilita "deshacer fusión" en el panel). */
  isMerged: boolean;
}

/** Respuesta de todas las operaciones de paleta: siempre la última versión vigente de la sesión. */
export interface ColorPaletteResponse {
  projectId: string;
  imageId: string;
  paletteId: string;
  version: number;
  tolerance: number;
  maxColors: number | null;
  sourceWidthPx: number;
  sourceHeightPx: number;
  transparentPercent: number;
  groups: ColorGroupPayload[];
  previewUrl: string;
  isConfirmed: boolean;
  cached: boolean;
}

/**
 * Code es estable y se mapea a copy en React sin parsear message. Valores que
 * pueden devolver los endpoints de paleta de colores.
 */
export type ColorPaletteErrorCode =
  | "invalid_parameters"
  | "not_found"
  | "group_not_found"
  | "palette_confirmed"
  | "not_merged"
  | "empty_palette"
  | "corrupt_image"
  | "dimensions_exceeded"
  | "timeout"
  | "engine_unavailable"
  | "invalid_response"
  | "processing_error"
  | "storage_failure"
  | "internal_error"
  | "network_error";
