import { describe, expect, it } from "vitest";
import { decomposeMatrix, parseLayerSvg, parseSvgTransformAttribute } from "./svgTransform";

describe("parseSvgTransformAttribute", () => {
  it("sin atributo -> matriz identidad", () => {
    expect(parseSvgTransformAttribute(null)).toEqual({ a: 1, b: 0, c: 0, d: 1, e: 0, f: 0 });
    expect(parseSvgTransformAttribute(undefined)).toEqual({ a: 1, b: 0, c: 0, d: 1, e: 0, f: 0 });
    expect(parseSvgTransformAttribute("")).toEqual({ a: 1, b: 0, c: 0, d: 1, e: 0, f: 0 });
  });

  it("translate(x,y) -- el caso real que emite el pipeline de vectorización", () => {
    const matrix = parseSvgTransformAttribute("translate(160,120)");
    expect(matrix).toEqual({ a: 1, b: 0, c: 0, d: 1, e: 160, f: 120 });
  });

  it("translate(x) con un solo argumento asume ty=0", () => {
    expect(parseSvgTransformAttribute("translate(50)")).toEqual({ a: 1, b: 0, c: 0, d: 1, e: 50, f: 0 });
  });

  it("scale(s) uniforme", () => {
    expect(parseSvgTransformAttribute("scale(2)")).toEqual({ a: 2, b: 0, c: 0, d: 2, e: 0, f: 0 });
  });

  it("matrix(a,b,c,d,e,f) directa", () => {
    expect(parseSvgTransformAttribute("matrix(1,0,0,1,5,10)")).toEqual({ a: 1, b: 0, c: 0, d: 1, e: 5, f: 10 });
  });

  it("una función desconocida se ignora sin romper el resto de la lista", () => {
    const matrix = parseSvgTransformAttribute("foo(1,2) translate(3,4)");
    expect(matrix).toEqual({ a: 1, b: 0, c: 0, d: 1, e: 3, f: 4 });
  });

  it("compone varias funciones en orden SVG estándar (izquierda = más externa)", () => {
    // translate(10,0) scale(2) sobre el punto (1,0): scale primero -> (2,0), luego translate -> (12,0)
    const matrix = parseSvgTransformAttribute("translate(10,0) scale(2)");
    const pointX = 1;
    const pointY = 0;
    const x = matrix.a * pointX + matrix.c * pointY + matrix.e;
    const y = matrix.b * pointX + matrix.d * pointY + matrix.f;
    expect(x).toBeCloseTo(12);
    expect(y).toBeCloseTo(0);
  });
});

describe("decomposeMatrix", () => {
  it("identidad -> sin traslación/rotación, escala 1", () => {
    expect(decomposeMatrix({ a: 1, b: 0, c: 0, d: 1, e: 0, f: 0 })).toEqual({
      x: 0,
      y: 0,
      rotation: 0,
      scaleX: 1,
      scaleY: 1,
      skewX: 0,
    });
  });

  it("solo traslación", () => {
    const props = decomposeMatrix({ a: 1, b: 0, c: 0, d: 1, e: 160, f: 120 });
    expect(props.x).toBe(160);
    expect(props.y).toBe(120);
    expect(props.rotation).toBeCloseTo(0);
    expect(props.scaleX).toBeCloseTo(1);
    expect(props.scaleY).toBeCloseTo(1);
  });

  it("rotación de 90 grados", () => {
    const matrix = parseSvgTransformAttribute("rotate(90)");
    const props = decomposeMatrix(matrix);
    expect(props.rotation).toBeCloseTo(90);
    expect(props.scaleX).toBeCloseTo(1);
    expect(props.scaleY).toBeCloseTo(1);
  });

  it("escala no uniforme", () => {
    const matrix = parseSvgTransformAttribute("scale(2,3)");
    const props = decomposeMatrix(matrix);
    expect(props.scaleX).toBeCloseTo(2);
    expect(props.scaleY).toBeCloseTo(3);
  });
});

describe("parseLayerSvg", () => {
  it("SVG plano real del pipeline (un <path> con translate) -- caso principal", () => {
    const svg = `<svg xmlns="http://www.w3.org/2000/svg" version="1.1" width="320" height="240">
<path d="M0,0 L160,0 L160,120 L0,120 Z " fill="#ffff00" transform="translate(160,120)" />
</svg>`;

    const result = parseLayerSvg(svg, "#000000");

    expect(result.width).toBe(320);
    expect(result.height).toBe(240);
    expect(result.paths).toHaveLength(1);
    expect(result.paths[0].d).toBe("M0,0 L160,0 L160,120 L0,120 Z ");
    expect(result.paths[0].fill).toBe("#ffff00");
    expect(result.paths[0].transform.x).toBe(160);
    expect(result.paths[0].transform.y).toBe(120);
  });

  it("múltiples <path> -- todos parseados en orden de documento", () => {
    const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="240" height="240">
<path d="M0,0 Z" fill="#000000" transform="translate(67,34)" />
<path d="M1,1 Z" fill="#000000" transform="translate(172,139)" />
</svg>`;

    const result = parseLayerSvg(svg, "#123456");
    expect(result.paths).toHaveLength(2);
    expect(result.paths[0].transform.x).toBe(67);
    expect(result.paths[1].transform.x).toBe(172);
  });

  it("path sin fill propio usa el colorHex de la capa del dominio (fallback, nunca inventado)", () => {
    const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="10" height="10"><path d="M0,0 Z" /></svg>`;
    const result = parseLayerSvg(svg, "#ff00ff");
    expect(result.paths[0].fill).toBe("#ff00ff");
  });

  it("path sin transform -> identidad (x=0,y=0)", () => {
    const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="10" height="10"><path d="M0,0 Z" fill="#111" /></svg>`;
    const result = parseLayerSvg(svg, "#000");
    expect(result.paths[0].transform.x).toBe(0);
    expect(result.paths[0].transform.y).toBe(0);
  });

  it("SVG inválido no lanza: devuelve un documento vacío", () => {
    expect(parseLayerSvg("no soy xml <<< >", "#000")).toEqual({ width: 0, height: 0, paths: [] });
  });

  it("SVG sin ningún <path> -> lista vacía, sin lanzar", () => {
    const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="10" height="10"></svg>`;
    expect(parseLayerSvg(svg, "#000")).toEqual({ width: 10, height: 10, paths: [] });
  });
});
