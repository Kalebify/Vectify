/**
 * Contratos tipados que expone ASP.NET Core para la detección/reducción de
 * paleta de colores (M2-S01): POST .../color-palette/detect, .../merge,
 * .../unmerge, .../rename, .../confirm, GET .../color-palette/{paletteId}.
 * Deben reflejar exactamente Vectorify.Api.Contracts.ColorPaletteResponse/
 * ColorGroupPayload/*Request del backend.
 */

export interface RgbColor {
  r: number;
  g: number;
  b: number;
}

export interface ColorGroupPayload {
  groupId: string;
  name: string;
  colorHex: string;
  /** M2.1-S02: mismo color que colorHex, ya descompuesto en componentes RGB (aditivo). */
  rgb: RgbColor;
  pixelCount: number;
  areaPercent: number;
  hasPartialAlpha: boolean;
  /**
   * M2.1-S02 (NUEVO): incluido (false)/excluido (true), independiente de un merge. El fondo
   * dominante detectado automáticamente viene pre-marcado en true en la primera detección, pero
   * es solo una sugerencia -- el usuario puede cambiarlo en cualquier momento (ver
   * ColorSwatchList, toggle "Incluir en el corte"/"Excluir del corte").
   */
  isExcluded: boolean;
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
  /** M2.1-S02: umbral de "grupo diminuto" que atacó la explosión de colores por antialiasing. */
  tinyAreaRatio: number;
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
