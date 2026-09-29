import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { EditorLayersPanel } from "./EditorLayersPanel";
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

describe("EditorLayersPanel — proyecto sin layers", () => {
  it("muestra un estado vacío honesto (sin filas inventadas)", () => {
    render(
      <EditorLayersPanel layers={[]} visibility={{}} onToggleVisibility={vi.fn()} selectedGroupId={null} onSelectGroup={vi.fn()} />,
    );
    expect(screen.getByText(/todavía no tiene capas generadas/)).toBeInTheDocument();
    expect(screen.queryByRole("list", { name: "Capas del documento" })).not.toBeInTheDocument();
  });
});

describe("EditorLayersPanel — proyecto multicolor", () => {
  it("una fila por capa, con swatch, nombre y operación", () => {
    render(
      <EditorLayersPanel
        layers={[layer(), layer({ groupId: "group-b", name: "Azul", colorHex: "#0000ff", manufacturingOperation: "engrave" })]}
        visibility={{ "group-a": true, "group-b": true }}
        onToggleVisibility={vi.fn()}
        selectedGroupId={null}
        onSelectGroup={vi.fn()}
      />,
    );

    expect(screen.getByText("Rojo")).toBeInTheDocument();
    expect(screen.getByText("Azul")).toBeInTheDocument();
    expect(screen.getByText("Corte")).toBeInTheDocument();
    expect(screen.getByText("Grabado")).toBeInTheDocument();
  });

  it("togglear el ojo llama a onToggleVisibility con el groupId correcto", () => {
    const onToggleVisibility = vi.fn();
    render(
      <EditorLayersPanel
        layers={[layer()]}
        visibility={{ "group-a": true }}
        onToggleVisibility={onToggleVisibility}
        selectedGroupId={null}
        onSelectGroup={vi.fn()}
      />,
    );

    fireEvent.click(screen.getByRole("button", { name: "Ocultar la capa Rojo" }));
    expect(onToggleVisibility).toHaveBeenCalledWith("group-a");
  });

  it("seleccionar una capa llama a onSelectGroup y la resalta (no solo por color)", () => {
    const onSelectGroup = vi.fn();
    render(
      <EditorLayersPanel
        layers={[layer()]}
        visibility={{ "group-a": true }}
        onToggleVisibility={vi.fn()}
        selectedGroupId={null}
        onSelectGroup={onSelectGroup}
      />,
    );

    fireEvent.click(screen.getByRole("button", { name: "Seleccionar la capa Rojo" }));
    expect(onSelectGroup).toHaveBeenCalledWith("group-a");
  });

  it("la capa seleccionada muestra un check además del resaltado", () => {
    render(
      <EditorLayersPanel
        layers={[layer()]}
        visibility={{ "group-a": true }}
        onToggleVisibility={vi.fn()}
        selectedGroupId="group-a"
        onSelectGroup={vi.fn()}
      />,
    );
    expect(screen.getByRole("button", { name: "Seleccionar la capa Rojo" })).toHaveAttribute("aria-pressed", "true");
    expect(screen.getByText("✓")).toBeInTheDocument();
  });

  it('"+ ADD LAYER" está presente pero deshabilitado', () => {
    render(
      <EditorLayersPanel layers={[layer()]} visibility={{ "group-a": true }} onToggleVisibility={vi.fn()} selectedGroupId={null} onSelectGroup={vi.fn()} />,
    );
    expect(screen.getByRole("button", { name: "+ ADD LAYER" })).toBeDisabled();
  });
});
