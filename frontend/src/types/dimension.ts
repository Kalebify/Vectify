/**
 * Contratos tipados que expone ASP.NET Core para la etapa de dimensiones
 * físicas en mm (M1-S09): POST .../dimensions/apply, GET
 * .../dimensions/{id}. Deben reflejar exactamente
 * Vectify.Api.Contracts.DimensionRequest/DimensionResponse.
 */

/** Mismo criterio que M1-S08 (CheckSourceKind): se acepta un SVG ya vectorizado o ya simplificado. */
export type DimensionSourceKind = "vector" | "simplification";

/** Respuesta de POST .../dimensions/apply y GET .../dimensions/{id}. */
export interface DimensionResponse {
  projectId: string;
  imageId: string;
  dimensionId: string;
  svgUrl: string;
  sourceKind: DimensionSourceKind;
  sourceId: string;
  version: number;
  widthMm: number;
  heightMm: number;
  lockAspectRatio: boolean;
  /** Ancho/alto del lienzo interno del SVG de origen, en unidades internas (1 unidad = 1 px del raster vectorizado, nunca DPI/EXIF). */
  sourceWidthPx: number;
  sourceHeightPx: number;
  cached: boolean;
}

/**
 * Code es estable y se mapea a copy en React sin parsear message. Valores
 * que puede devolver POST .../dimensions/apply.
 */
export type DimensionErrorCode =
  | "invalid_parameters"
  | "dimension_out_of_range"
  | "not_found"
  | "storage_failure"
  | "invalid_source_svg"
  | "internal_error"
  | "network_error";
