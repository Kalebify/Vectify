import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { PaletteBar } from "./PaletteBar";
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
    visible: true,
    locked: false,
    areaPercent: 60,
    hasPartialAlpha: false,
    isExcluded: false,
    ...overrides,
  };
}

describe("PaletteBar — estado vacío", () => {
  it("sin capas muestra un mensaje honesto, no swatches inventados", () => {
    render(<PaletteBar layers={[]} selectedGroupId={null} onSelectGroup={vi.fn()} />);
    expect(screen.getByText("Sin paleta confirmada")).toBeInTheDocument();
  });
});

describe("PaletteBar — swatches de la paleta confirmada", () => {
  it("un swatch por capa, con nombre accesible y color real", () => {
    render(
      <PaletteBar
        layers={[layer(), layer({ groupId: "group-b", name: "Azul", colorHex: "#0000ff" })]}
        selectedGroupId={null}
        onSelectGroup={vi.fn()}
      />,
    );

    expect(screen.getByRole("button", { name: /Rojo \(#ff0000\)/ })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Azul \(#0000ff\)/ })).toBeInTheDocument();
  });

  it("clic en un swatch selecciona ese groupId (mismo groupId compartido con el resto)", () => {
    const onSelectGroup = vi.fn();
    render(<PaletteBar layers={[layer()]} selectedGroupId={null} onSelectGroup={onSelectGroup} />);

    fireEvent.click(screen.getByRole("button", { name: /Rojo/ }));
    expect(onSelectGroup).toHaveBeenCalledWith("group-a");
  });

  it("el swatch seleccionado se marca con aria-pressed Y un check visual, no solo color", () => {
    render(<PaletteBar layers={[layer()]} selectedGroupId="group-a" onSelectGroup={vi.fn()} />);

    const button = screen.getByRole("button", { name: /Rojo/ });
    expect(button).toHaveAttribute("aria-pressed", "true");
    expect(button.querySelector(".palette-bar__swatch-check")).toBeInTheDocument();
  });

  it('el botón "+" está presente pero deshabilitado (agregar color es MVP3)', () => {
    render(<PaletteBar layers={[layer()]} selectedGroupId={null} onSelectGroup={vi.fn()} />);
    expect(screen.getByRole("button", { name: /Agregar color/ })).toBeDisabled();
  });
});
