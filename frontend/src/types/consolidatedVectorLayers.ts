/**
 * Contratos tipados del modelo `VectorLayer` CONSOLIDADO (M2.1-S03, extendido
 * en M2.1-S04 con `pathCount`): GET
 * .../color-palette/{paletteId}/layers/consolidated. Deben reflejar
 * exactamente Vectorify.Api.Contracts.ConsolidatedVectorLayerSetResponse/
 * ConsolidatedVectorLayerPayload/RasterValidationPayload.
 */

export interface RasterValidationPayload {
  ownMismatchRatio: number;
  ownMismatchTolerance: number;
  ownMismatchWithinTolerance: boolean;
  contaminationRatio: number;
  contaminationTolerance: number;
  contaminationWithinTolerance: boolean;
  warnings: string[];
}

export interface ConsolidatedVectorLayerPayload {
  id: string;
  name: string;
  colorHex: string;
  fill: string;
  vectorId: string;
  svgUrl: string;
  /** Número de `<path>` del SVG de esta capa (M1-S05, lectura trivial). */
  pathCount: number;
  /** null si M2-S03 todavía no calculó los componentes físicos de esta capa. */
  componentCount: number | null;
  /** "cut" | "engrave" | "ignore" | "unassigned" (M2-S07). */
  manufacturingOperation: string;
  visible: boolean;
  locked: boolean;
  order: number;
  rasterValidation: RasterValidationPayload;
}

export interface ConsolidatedVectorLayerSetResponse {
  projectId: string;
  imageId: string;
  layerSetId: string;
  version: number;
  paletteId: string;
  paletteVersion: number;
  sourceWidthPx: number;
  sourceHeightPx: number;
  layers: ConsolidatedVectorLayerPayload[];
}
