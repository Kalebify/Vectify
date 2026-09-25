import { describe, expect, it } from "vitest";
import { highlightSvgPath } from "./highlightSvgPath";

const TWO_PATH_SVG =
  '<svg xmlns="http://www.w3.org/2000/svg" width="100" height="100">' +
  '<path d="M0,0 L10,0 L10,10 L0,10 Z"/>' +
  '<path d="M20,20 L30,20 L30,30 L20,30 Z"/>' +
  "</svg>";

describe("highlightSvgPath", () => {
  it("agrega atributos de presentación al path del índice pedido", () => {
    const highlighted = highlightSvgPath(TWO_PATH_SVG, [0]);

    const doc = new DOMParser().parseFromString(highlighted, "image/svg+xml");
    const paths = doc.getElementsByTagName("path");

    expect(paths[0].getAttribute("stroke")).toBeTruthy();
    expect(paths[0].getAttribute("fill-opacity")).toBeTruthy();
    expect(paths[1].getAttribute("stroke")).toBeNull();
  });

  it("resalta múltiples índices a la vez (grupo de duplicados)", () => {
    const highlighted = highlightSvgPath(TWO_PATH_SVG, [0, 1]);

    const doc = new DOMParser().parseFromString(highlighted, "image/svg+xml");
    const paths = doc.getElementsByTagName("path");

    expect(paths[0].getAttribute("stroke")).toBeTruthy();
    expect(paths[1].getAttribute("stroke")).toBeTruthy();
  });

  it("no toca los demás atributos del path resaltado", () => {
    const svg =
      '<svg xmlns="http://www.w3.org/2000/svg" width="10" height="10"><path d="M0,0 L10,0 L10,10 Z" fill="#000000"/></svg>';

    const highlighted = highlightSvgPath(svg, [0]);

    const doc = new DOMParser().parseFromString(highlighted, "image/svg+xml");
    expect(doc.getElementsByTagName("path")[0].getAttribute("d")).toBe("M0,0 L10,0 L10,10 Z");
  });

  it("devuelve el SVG sin cambios si el índice no existe", () => {
    const highlighted = highlightSvgPath(TWO_PATH_SVG, [99]);

    expect(highlighted).toBe(TWO_PATH_SVG);
  });

  it("devuelve el SVG sin cambios si no se piden índices", () => {
    const highlighted = highlightSvgPath(TWO_PATH_SVG, []);

    expect(highlighted).toBe(TWO_PATH_SVG);
  });

  it("nunca lanza ante un SVG malformado: devuelve el texto de entrada intacto", () => {
    const malformed = "<svg><path d='M0,0'";

    expect(() => highlightSvgPath(malformed, [0])).not.toThrow();
    expect(highlightSvgPath(malformed, [0])).toBe(malformed);
  });

  it("es determinista", () => {
    const first = highlightSvgPath(TWO_PATH_SVG, [1]);
    const second = highlightSvgPath(TWO_PATH_SVG, [1]);

    expect(first).toBe(second);
  });
});
