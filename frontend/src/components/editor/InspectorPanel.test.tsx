import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { InspectorPanel } from "./InspectorPanel";
import type { VectorDocumentLayer } from "../../hooks/useVectorDocument";

function layer(overrides: Partial<VectorDocumentLayer> = {}): VectorDocumentLayer {
  return {
    groupId: "group-a",
    name: "Rojo",
    colorHex: "#ff0000",
    fill: "#ff0000",
    vectorId: "vector-a",
    svgUrl: "/vectors/a",
    pathCount: 3,
    componentCount: 1,
    manufacturingOperation: "cut",
    order: 0,
    areaPercent: 60,
    hasPartialAlpha: false,
    isExcluded: false,
    ...overrides,
  };
}

describe("InspectorPanel — sin selección", () => {
  it("muestra el hint de LayerInfoPanel sin inventar datos de ninguna capa", () => {
    render(<InspectorPanel layers={[]} selectedLayer={null} isVisible={false} onIsolate={vi.fn()} onShowAll={vi.fn()} onRefresh={vi.fn()} />);
    expect(screen.getByText(/Seleccioná un color en la paleta/)).toBeInTheDocument();
  });
});

describe("InspectorPanel — capa seleccionada (Color / Paths / Pieces / Operation del wireframe)", () => {
  it("muestra HEX, paths, componentes y operación de la capa seleccionada", () => {
    render(
      <InspectorPanel
        layers={[layer()]}
        selectedLayer={layer()}
        isVisible
        onIsolate={vi.fn()}
        onShowAll={vi.fn()}
        onRefresh={vi.fn()}
      />,
    );

    expect(screen.getByText("#ff0000")).toBeInTheDocument();
    expect(screen.getByText("3")).toBeInTheDocument();
    expect(screen.getAllByText("1").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Corte").length).toBeGreaterThan(0);
  });

  it("Aislar llama a onIsolate", () => {
    const onIsolate = vi.fn();
    render(<InspectorPanel layers={[layer()]} selectedLayer={layer()} isVisible onIsolate={onIsolate} onShowAll={vi.fn()} onRefresh={vi.fn()} />);
    fireEvent.click(screen.getByRole("button", { name: "Aislar" }));
    expect(onIsolate).toHaveBeenCalled();
  });

  it("suma el resumen de operaciones de fabricación (M2-S07) calculado a partir de las capas del documento", () => {
    render(
      <InspectorPanel
        layers={[layer(), layer({ groupId: "group-b", manufacturingOperation: "engrave" })]}
        selectedLayer={layer()}
        isVisible
        onIsolate={vi.fn()}
        onShowAll={vi.fn()}
        onRefresh={vi.fn()}
      />,
    );

    expect(screen.getByText(/1 en Corte, 1 en Grabado/)).toBeInTheDocument();
  });
});
