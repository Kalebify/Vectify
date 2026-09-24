import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { IDENTITY_TRANSFORM } from "../../hooks/useCanvasTransform";
import { VectorCanvas } from "./VectorCanvas";

function renderCanvas(overrides: Partial<React.ComponentProps<typeof VectorCanvas>> = {}) {
  const onZoomBy = vi.fn();
  const onPanBy = vi.fn();
  const onMeasure = vi.fn();

  const utils = render(
    <VectorCanvas
      label="SVG vectorizado"
      src="/vector.svg"
      alt="SVG vectorizado de logo.png"
      intrinsicWidth={200}
      intrinsicHeight={100}
      transform={IDENTITY_TRANSFORM}
      onZoomBy={onZoomBy}
      onPanBy={onPanBy}
      onMeasure={onMeasure}
      {...overrides}
    />,
  );

  const container = screen.getByLabelText(/SVG vectorizado\./);
  return { ...utils, container, onZoomBy, onPanBy, onMeasure };
}

describe("VectorCanvas — render", () => {
  it("renderiza la imagen con el tamaño intrínseco como atributos width/height", () => {
    renderCanvas();
    const img = screen.getByRole("img", { name: "SVG vectorizado de logo.png" });
    expect(img).toHaveAttribute("src", "/vector.svg");
    expect(img).toHaveAttribute("width", "200");
    expect(img).toHaveAttribute("height", "100");
  });

  it("nunca modifica el src: solo aplica transform CSS", () => {
    const { rerender } = renderCanvas();
    const img = screen.getByRole("img", { name: "SVG vectorizado de logo.png" });
    expect(img.style.transform).toContain("scale(1)");

    rerender(
      <VectorCanvas
        label="SVG vectorizado"
        src="/vector.svg"
        alt="SVG vectorizado de logo.png"
        intrinsicWidth={200}
        intrinsicHeight={100}
        transform={{ scale: 2, panX: 10, panY: -5 }}
        onZoomBy={vi.fn()}
        onPanBy={vi.fn()}
      />,
    );

    expect(img).toHaveAttribute("src", "/vector.svg");
    expect(img.style.transform).toContain("scale(2)");
    expect(img.style.transform).toContain("translate(10px, -5px)");
  });
});

describe("VectorCanvas — zoom con rueda del mouse", () => {
  it("calcula el anchor relativo al centro del contenedor y llama a onZoomBy", () => {
    const { container, onZoomBy } = renderCanvas();

    vi.spyOn(container, "getBoundingClientRect").mockReturnValue({
      left: 0,
      top: 0,
      width: 200,
      height: 100,
      right: 200,
      bottom: 100,
      x: 0,
      y: 0,
      toJSON() {
        return this;
      },
    } as DOMRect);

    fireEvent.wheel(container, { deltaY: -100, clientX: 150, clientY: 60 });

    expect(onZoomBy).toHaveBeenCalledTimes(1);
    const [factor, anchor] = onZoomBy.mock.calls[0];
    expect(factor).toBeGreaterThan(1); // deltaY negativo (scroll hacia arriba) = acercar
    expect(anchor).toEqual({ x: 50, y: 10 }); // (150,60) menos el centro (100,50)
  });
});

describe("VectorCanvas — pan con arrastre (pointer events)", () => {
  it("acumula el delta de pointermove y lo aplica vía requestAnimationFrame", async () => {
    const { container, onPanBy } = renderCanvas();

    fireEvent.pointerDown(container, { pointerId: 1, clientX: 10, clientY: 10 });
    fireEvent.pointerMove(container, { pointerId: 1, clientX: 25, clientY: 5 });
    fireEvent.pointerMove(container, { pointerId: 1, clientX: 30, clientY: 0 });

    await new Promise((resolve) => requestAnimationFrame(resolve));

    expect(onPanBy).toHaveBeenCalledTimes(1);
    expect(onPanBy).toHaveBeenCalledWith(20, -10); // (25-10)+(30-25) , (5-10)+(0-5)
  });

  it("ignora pointermove de un pointerId distinto al que inició el arrastre", async () => {
    const { container, onPanBy } = renderCanvas();

    fireEvent.pointerDown(container, { pointerId: 1, clientX: 0, clientY: 0 });
    fireEvent.pointerMove(container, { pointerId: 2, clientX: 100, clientY: 100 });

    await new Promise((resolve) => requestAnimationFrame(resolve));

    expect(onPanBy).not.toHaveBeenCalled();
  });
});

describe("VectorCanvas — teclado", () => {
  it("flechas desplazan y +/- hacen zoom cuando el contenedor tiene foco", () => {
    const { container, onPanBy, onZoomBy } = renderCanvas();

    fireEvent.keyDown(container, { key: "ArrowRight" });
    fireEvent.keyDown(container, { key: "ArrowUp" });
    fireEvent.keyDown(container, { key: "+" });
    fireEvent.keyDown(container, { key: "-" });

    expect(onPanBy).toHaveBeenNthCalledWith(1, -40, 0);
    expect(onPanBy).toHaveBeenNthCalledWith(2, 0, 40);
    expect(onZoomBy).toHaveBeenNthCalledWith(1, 1.25);
    expect(onZoomBy).toHaveBeenNthCalledWith(2, 1 / 1.25);
  });

  it("es alcanzable por teclado (tabIndex=0) y expone una descripción accesible", () => {
    const { container } = renderCanvas();
    expect(container).toHaveAttribute("tabindex", "0");
    expect(container.getAttribute("aria-label")).toMatch(/SVG vectorizado\./);
  });
});
