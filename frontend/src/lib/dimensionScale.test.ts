import { describe, expect, it } from "vitest";
import { MAX_DIMENSION_MM, MIN_DIMENSION_MM, computeDimensionPreview, parseMmInput } from "./dimensionScale";

describe("computeDimensionPreview — proporción bloqueada", () => {
  it("deriva el mismo alto que el ancho para una fuente cuadrada (1:1)", () => {
    const result = computeDimensionPreview({
      sourceWidthPx: 10,
      sourceHeightPx: 10,
      widthMm: 100,
      heightMm: null,
      lockAspectRatio: true,
    });

    expect(result).toEqual({ ok: true, widthMm: 100, heightMm: 100 });
  });

  it("deriva un alto proporcionalmente menor para una fuente muy ancha", () => {
    const result = computeDimensionPreview({
      sourceWidthPx: 200,
      sourceHeightPx: 50,
      widthMm: 400,
      heightMm: null,
      lockAspectRatio: true,
    });

    expect(result.ok).toBe(true);
    expect(result).toMatchObject({ widthMm: 400, heightMm: 100 });
  });

  it("deriva un ancho proporcionalmente menor a partir del alto para una fuente muy alta", () => {
    const result = computeDimensionPreview({
      sourceWidthPx: 50,
      sourceHeightPx: 200,
      widthMm: null,
      heightMm: 400,
      lockAspectRatio: true,
    });

    expect(result.ok).toBe(true);
    expect(result).toMatchObject({ widthMm: 100, heightMm: 400 });
  });

  it("rechaza cuando no se completó ni ancho ni alto", () => {
    const result = computeDimensionPreview({
      sourceWidthPx: 10,
      sourceHeightPx: 10,
      widthMm: null,
      heightMm: null,
      lockAspectRatio: true,
    });

    expect(result.ok).toBe(false);
  });

  it("rechaza cuando se completaron ambos (ambigüedad con la proporción bloqueada)", () => {
    const result = computeDimensionPreview({
      sourceWidthPx: 10,
      sourceHeightPx: 10,
      widthMm: 100,
      heightMm: 50,
      lockAspectRatio: true,
    });

    expect(result.ok).toBe(false);
  });

  it("rechaza cuando el valor derivado queda por debajo del mínimo", () => {
    // Fuente muy ancha (1000:1): el ancho mínimo permitido deriva un alto por debajo de MIN_DIMENSION_MM.
    const result = computeDimensionPreview({
      sourceWidthPx: 1000,
      sourceHeightPx: 1,
      widthMm: MIN_DIMENSION_MM,
      heightMm: null,
      lockAspectRatio: true,
    });

    expect(result.ok).toBe(false);
  });

  it("rechaza cuando el valor derivado queda por encima del máximo", () => {
    // Fuente muy alta (1:1000): el ancho máximo permitido deriva un alto por encima de MAX_DIMENSION_MM.
    const result = computeDimensionPreview({
      sourceWidthPx: 1,
      sourceHeightPx: 1000,
      widthMm: MAX_DIMENSION_MM,
      heightMm: null,
      lockAspectRatio: true,
    });

    expect(result.ok).toBe(false);
  });
});

describe("computeDimensionPreview — proporción desbloqueada", () => {
  it("acepta ancho y alto independientes, incluso si deforman el diseño", () => {
    const result = computeDimensionPreview({
      sourceWidthPx: 10,
      sourceHeightPx: 10,
      widthMm: 300,
      heightMm: 20,
      lockAspectRatio: false,
    });

    expect(result).toEqual({ ok: true, widthMm: 300, heightMm: 20 });
  });

  it("rechaza cuando falta uno de los dos valores", () => {
    const result = computeDimensionPreview({
      sourceWidthPx: 10,
      sourceHeightPx: 10,
      widthMm: 300,
      heightMm: null,
      lockAspectRatio: false,
    });

    expect(result.ok).toBe(false);
  });
});

describe("computeDimensionPreview — valores límite", () => {
  it("acepta el mínimo permitido", () => {
    const result = computeDimensionPreview({
      sourceWidthPx: 10,
      sourceHeightPx: 10,
      widthMm: MIN_DIMENSION_MM,
      heightMm: null,
      lockAspectRatio: true,
    });

    expect(result.ok).toBe(true);
  });

  it("acepta el máximo permitido", () => {
    const result = computeDimensionPreview({
      sourceWidthPx: 10,
      sourceHeightPx: 10,
      widthMm: MAX_DIMENSION_MM,
      heightMm: null,
      lockAspectRatio: true,
    });

    expect(result.ok).toBe(true);
  });

  it("rechaza 0", () => {
    const result = computeDimensionPreview({
      sourceWidthPx: 10,
      sourceHeightPx: 10,
      widthMm: 0,
      heightMm: null,
      lockAspectRatio: true,
    });

    expect(result.ok).toBe(false);
  });

  it("rechaza un valor negativo", () => {
    const result = computeDimensionPreview({
      sourceWidthPx: 10,
      sourceHeightPx: 10,
      widthMm: -10,
      heightMm: null,
      lockAspectRatio: true,
    });

    expect(result.ok).toBe(false);
  });

  it("rechaza un valor por encima del máximo", () => {
    const result = computeDimensionPreview({
      sourceWidthPx: 10,
      sourceHeightPx: 10,
      widthMm: MAX_DIMENSION_MM + 0.1,
      heightMm: null,
      lockAspectRatio: true,
    });

    expect(result.ok).toBe(false);
  });
});

describe("parseMmInput", () => {
  it("un string vacío es 'campo no completado' (null)", () => {
    expect(parseMmInput("")).toBeNull();
  });

  it("un string con solo espacios es null", () => {
    expect(parseMmInput("   ")).toBeNull();
  });

  it("parsea un número válido", () => {
    expect(parseMmInput("123.5")).toBe(123.5);
  });

  it("un valor no numérico es null", () => {
    expect(parseMmInput("abc")).toBeNull();
  });
});
