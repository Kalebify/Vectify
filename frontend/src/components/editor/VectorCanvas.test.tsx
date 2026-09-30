import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { IDENTITY_TRANSFORM } from "../../hooks/useCanvasTransform";
import type { VectorDocumentLayer } from "../../hooks/useVectorDocument";
import { VectorCanvas } from "./VectorCanvas";

function layer(overrides: Partial<VectorDocumentLayer> = {}): VectorDocumentLayer {
  return {
    groupId: "group-a",
    name: "Rojo",
    colorHex: "#ff0000",
    fill: "#ff0000",
    vectorId: "vector-a",
    svgUrl: "/vectors/a",
    pathCount: 1,
    componentCount: 1,
    manufacturingOperation: "cut",
    order: 0,
    visible: true,
    locked: false,
    areaPercent: 60,
    hasPartialAlpha: false,
    isExcluded: false,
    ...overrides,
  };
}

const SVG_TEXT = `<svg xmlns="http://www.w3.org/2000/svg" width="320" height="240"><path d="M0,0 L10,0 L10,10 Z" fill="#ff0000" transform="translate(5,5)" /></svg>`;

// jsdom no implementa ResizeObserver con mediciones reales (ver
// src/test/setup.ts): acá se sobreescribe SOLO en este archivo con un stub
// que dispara la medición inicial de forma síncrona, del mismo tamaño que
// un contenedor real tendría -- necesario porque VectorCanvas no monta el
// <Stage> de Konva hasta tener una medida real del contenedor (evita
// dividir por cero / un Stage de 0x0).
class ImmediateResizeObserver {
  callback: ResizeObserverCallback;
  constructor(callback: ResizeObserverCallback) {
    this.callback = callback;
  }
  observe() {
    this.callback(
      [{ contentRect: { width: 800, height: 600 } } as ResizeObserverEntry],
      this as unknown as ResizeObserver,
    );
  }
  unobserve() {}
  disconnect() {}
}

function renderCanvas(overrides: Partial<React.ComponentProps<typeof VectorCanvas>> = {}) {
  const onZoomBy = vi.fn();
  const onPanBy = vi.fn();
  const onMeasure = vi.fn();
  const onSelectGroup = vi.fn();

  const utils = render(
    <VectorCanvas
      layers={[layer()]}
      visibility={{ "group-a": true }}
      sourceWidthPx={320}
      sourceHeightPx={240}
      selectedGroupId={null}
      onSelectGroup={onSelectGroup}
      tool="select"
      transform={IDENTITY_TRANSFORM}
      onZoomBy={onZoomBy}
      onPanBy={onPanBy}
      onMeasure={onMeasure}
      {...overrides}
    />,
  );

  return { ...utils, onZoomBy, onPanBy, onMeasure, onSelectGroup };
}

describe("VectorCanvas", () => {
  beforeEach(() => {
    vi.stubGlobal("ResizeObserver", ImmediateResizeObserver);
    vi.stubGlobal("fetch", vi.fn(() => Promise.resolve(new Response(SVG_TEXT, { status: 200 }))));
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("proyecto sin capas: estado vacío honesto, sin Stage", () => {
    renderCanvas({ layers: [] });
    expect(screen.getByText("Ninguna capa visible.")).toBeInTheDocument();
  });

  it("proyecto multicolor: pide el SVG real de cada capa visible (nunca datos hardcodeados)", async () => {
    const fetchMock = vi.fn((_input: string | URL) => Promise.resolve(new Response(SVG_TEXT, { status: 200 })));
    vi.stubGlobal("fetch", fetchMock);

    renderCanvas({
      layers: [layer(), layer({ groupId: "group-b", vectorId: "vector-b", svgUrl: "/vectors/b" })],
      visibility: { "group-a": true, "group-b": true },
    });

    await waitFor(
      () => {
        const requestedUrls = fetchMock.mock.calls.map((call) => String(call[0]));
        expect(requestedUrls).toEqual(expect.arrayContaining(["/vectors/a", "/vectors/b"]));
      },
      { timeout: 25000, interval: 100 },
    );
  }, 30000);

  it("reporta el tamaño medido del contenedor vía onMeasure (resize)", async () => {
    const { onMeasure } = renderCanvas();
    await waitFor(() => expect(onMeasure).toHaveBeenCalledWith({ width: 800, height: 600 }));
  });

  it("la rueda del mouse llama a onZoomBy con un factor y un ancla relativa al centro", async () => {
    const { onZoomBy } = renderCanvas();
    const canvasEl = await screen.findByRole("application");

    vi.spyOn(canvasEl, "getBoundingClientRect").mockReturnValue({
      left: 0,
      top: 0,
      width: 800,
      height: 600,
      right: 800,
      bottom: 600,
      x: 0,
      y: 0,
      toJSON() {
        return this;
      },
    } as DOMRect);

    fireEvent.wheel(canvasEl, { deltaY: -100, clientX: 400, clientY: 300 });

    expect(onZoomBy).toHaveBeenCalled();
    const [factor, anchor] = onZoomBy.mock.calls[0];
    expect(factor).toBeGreaterThan(1);
    expect(anchor).toEqual({ x: 0, y: 0 });
  });

  it("con la herramienta Pan, arrastrar llama a onPanBy", async () => {
    const { onPanBy } = renderCanvas({ tool: "pan" });
    const canvasEl = await screen.findByRole("application");

    fireEvent.pointerDown(canvasEl, { pointerId: 1, clientX: 100, clientY: 100 });
    fireEvent.pointerMove(canvasEl, { pointerId: 1, clientX: 120, clientY: 90 });

    await waitFor(() => expect(onPanBy).toHaveBeenCalled());
  });

  it("con la herramienta Select, arrastrar NO llama a onPanBy", async () => {
    const { onPanBy } = renderCanvas({ tool: "select" });
    const canvasEl = await screen.findByRole("application");

    fireEvent.pointerDown(canvasEl, { pointerId: 1, clientX: 100, clientY: 100 });
    fireEvent.pointerMove(canvasEl, { pointerId: 1, clientX: 120, clientY: 90 });

    expect(onPanBy).not.toHaveBeenCalled();
  });

  it("mantener Espacio activa el pan temporalmente (shortcut documentado)", async () => {
    const { onPanBy } = renderCanvas({ tool: "select" });
    const canvasEl = await screen.findByRole("application");

    fireEvent.keyDown(canvasEl, { key: " " });
    fireEvent.pointerDown(canvasEl, { pointerId: 1, clientX: 100, clientY: 100 });
    fireEvent.pointerMove(canvasEl, { pointerId: 1, clientX: 130, clientY: 100 });

    await waitFor(() => expect(onPanBy).toHaveBeenCalled());
  });

  it("+ / - de teclado llaman a onZoomBy", async () => {
    const { onZoomBy } = renderCanvas();
    const canvasEl = await screen.findByRole("application");

    fireEvent.keyDown(canvasEl, { key: "+" });
    expect(onZoomBy).toHaveBeenCalledWith(expect.any(Number));
  });
});
