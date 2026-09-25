/**
 * Cálculo puro del preview de dimensiones físicas (M1-S09), 100% en el
 * cliente y SIN round-trip a la Web API: dado que aplicar dimensiones es
 * aritmética simple de escala (ver Vectify.Api.Dimensioning.SvgDimensionWriter/
 * DimensionParameterValidator.ResolveDimensions -- este archivo replica esa
 * misma lógica en TypeScript), no hay ningún algoritmo ni motor externo cuyo
 * resultado no se pueda anticipar en el navegador. El resultado de esta
 * función es SOLO para mostrarle al usuario el tamaño final antes de
 * "Aplicar" -- la Web API vuelve a validar y calcular todo del lado del
 * servidor al aplicar, es la única fuente de verdad de lo que se persiste.
 *
 * Rango válido [MIN_DIMENSION_MM, MAX_DIMENSION_MM]: debe coincidir con
 * Vectify.Api.Options.DimensionOptions (1mm-1000mm, supuesto documentado en
 * el reporte del sprint) para que el preview no le muestre al usuario un
 * resultado que la Web API vaya a rechazar.
 */

export const MIN_DIMENSION_MM = 1;
export const MAX_DIMENSION_MM = 1000;

export interface DimensionPreviewInput {
  /** Ancho/alto del lienzo interno del SVG de origen (1 unidad = 1 px del raster vectorizado). */
  sourceWidthPx: number;
  sourceHeightPx: number;
  /** null = campo vacío/no completado todavía. */
  widthMm: number | null;
  heightMm: number | null;
  lockAspectRatio: boolean;
}

export type DimensionPreviewResult =
  | { ok: true; widthMm: number; heightMm: number }
  | { ok: false; reason: string };

/**
 * Calcula el ancho/alto final en mm a partir de lo que el usuario completó,
 * o explica por qué todavía no hay un resultado válido para mostrar/aplicar.
 * Con la proporción bloqueada (default), exige EXACTAMENTE uno de los dos
 * campos y deriva el otro a partir del aspect ratio real del SVG de origen;
 * desbloqueada, exige ambos (permite deformar el diseño explícitamente).
 */
export function computeDimensionPreview(input: DimensionPreviewInput): DimensionPreviewResult {
  const { sourceWidthPx, sourceHeightPx, widthMm, heightMm, lockAspectRatio } = input;

  if (lockAspectRatio) {
    const hasWidth = widthMm !== null;
    const hasHeight = heightMm !== null;

    if (hasWidth === hasHeight) {
      return {
        ok: false,
        reason: "Con la proporción bloqueada, ingresá el ancho o el alto (no ambos, no ninguno).",
      };
    }

    if (sourceWidthPx <= 0 || sourceHeightPx <= 0) {
      return { ok: false, reason: "El SVG de origen no tiene dimensiones válidas." };
    }

    const aspectWidthOverHeight = sourceWidthPx / sourceHeightPx;
    const resolvedWidthMm = hasWidth ? widthMm! : heightMm! * aspectWidthOverHeight;
    const resolvedHeightMm = hasWidth ? widthMm! / aspectWidthOverHeight : heightMm!;

    return validateRange(resolvedWidthMm, resolvedHeightMm);
  }

  if (widthMm === null || heightMm === null) {
    return { ok: false, reason: "Con la proporción desbloqueada, ingresá ancho y alto." };
  }

  return validateRange(widthMm, heightMm);
}

function validateRange(widthMm: number, heightMm: number): DimensionPreviewResult {
  if (!Number.isFinite(widthMm) || !Number.isFinite(heightMm)) {
    return { ok: false, reason: "El valor calculado no es un número válido." };
  }

  if (widthMm < MIN_DIMENSION_MM || widthMm > MAX_DIMENSION_MM) {
    return {
      ok: false,
      reason: `El ancho (${widthMm.toFixed(3)}mm) debe estar entre ${MIN_DIMENSION_MM} y ${MAX_DIMENSION_MM}mm.`,
    };
  }

  if (heightMm < MIN_DIMENSION_MM || heightMm > MAX_DIMENSION_MM) {
    return {
      ok: false,
      reason: `El alto (${heightMm.toFixed(3)}mm) debe estar entre ${MIN_DIMENSION_MM} y ${MAX_DIMENSION_MM}mm.`,
    };
  }

  return { ok: true, widthMm, heightMm };
}

/** Parsea un input de texto a mm: string vacío -> null (campo no completado), no-numérico -> null. */
export function parseMmInput(value: string): number | null {
  if (value.trim() === "") {
    return null;
  }

  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : null;
}
