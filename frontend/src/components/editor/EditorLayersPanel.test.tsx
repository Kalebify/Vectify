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
    visible: true,
    locked: false,
    areaPercent: 60,
    hasPartialAlpha: false,
    isExcluded: false,
    ...overrides,
  };
}

function dataTransferStub(payload: Record<string, string> = {}) {
  const store = new Map(Object.entries(payload));
  return {
    effectAllowed: "",
    dropEffect: "",
    setData: (format: string, value: string) => store.set(format, value),
    getData: (format: string) => store.get(format) ?? "",
  };
}

describe("EditorLayersPanel — proyecto sin layers", () => {
  it("muestra un estado vacío honesto (sin filas inventadas)", () => {
    render(
      <EditorLayersPanel
        layers={[]}
        visibility={{}}
        onToggleVisibility={vi.fn()}
        onToggleLocked={vi.fn()}
        onReorder={vi.fn()}
        selectedGroupId={null}
        onSelectGroup={vi.fn()}
      />,
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
        onToggleLocked={vi.fn()}
        onReorder={vi.fn()}
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
        onToggleLocked={vi.fn()}
        onReorder={vi.fn()}
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
        onToggleLocked={vi.fn()}
        onReorder={vi.fn()}
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
        onToggleLocked={vi.fn()}
        onReorder={vi.fn()}
        selectedGroupId="group-a"
        onSelectGroup={vi.fn()}
      />,
    );
    expect(screen.getByRole("button", { name: "Seleccionar la capa Rojo" })).toHaveAttribute("aria-pressed", "true");
    expect(screen.getByText("✓")).toBeInTheDocument();
  });

  it('"+ ADD LAYER" está presente pero deshabilitado', () => {
    render(
      <EditorLayersPanel
        layers={[layer()]}
        visibility={{ "group-a": true }}
        onToggleVisibility={vi.fn()}
        onToggleLocked={vi.fn()}
        onReorder={vi.fn()}
        selectedGroupId={null}
        onSelectGroup={vi.fn()}
      />,
    );
    expect(screen.getByRole("button", { name: "+ ADD LAYER" })).toBeDisabled();
  });
});

describe("EditorLayersPanel — Lock (M2.1-S07, concepto nuevo)", () => {
  it("una capa desbloqueada muestra el candado abierto y togglearlo llama a onToggleLocked", () => {
    const onToggleLocked = vi.fn();
    render(
      <EditorLayersPanel
        layers={[layer({ locked: false })]}
        visibility={{ "group-a": true }}
        onToggleVisibility={vi.fn()}
        onToggleLocked={onToggleLocked}
        onReorder={vi.fn()}
        selectedGroupId={null}
        onSelectGroup={vi.fn()}
      />,
    );

    const lockButton = screen.getByRole("button", { name: "Bloquear la capa Rojo" });
    expect(lockButton).toHaveAttribute("aria-pressed", "false");
    fireEvent.click(lockButton);
    expect(onToggleLocked).toHaveBeenCalledWith("group-a");
  });

  it("una capa bloqueada muestra el candado cerrado, pero Eye y Select siguen habilitados (Lock no bloquea visibilidad/inspección)", () => {
    render(
      <EditorLayersPanel
        layers={[layer({ locked: true })]}
        visibility={{ "group-a": true }}
        onToggleVisibility={vi.fn()}
        onToggleLocked={vi.fn()}
        onReorder={vi.fn()}
        selectedGroupId={null}
        onSelectGroup={vi.fn()}
      />,
    );

    expect(screen.getByRole("button", { name: "Desbloquear la capa Rojo" })).toHaveAttribute("aria-pressed", "true");
    expect(screen.getByRole("button", { name: "Ocultar la capa Rojo" })).toBeEnabled();
    expect(screen.getByRole("button", { name: "Seleccionar la capa Rojo" })).toBeEnabled();
  });
});

describe("EditorLayersPanel — Drag & Drop para reordenar (M2.1-S07)", () => {
  it("arrastrar una fila y soltarla sobre otra llama a onReorder con el nuevo orden completo de groupId", () => {
    const onReorder = vi.fn();
    const layers = [
      layer({ groupId: "group-a", name: "Rojo", order: 0 }),
      layer({ groupId: "group-b", name: "Azul", colorHex: "#0000ff", order: 1 }),
      layer({ groupId: "group-c", name: "Verde", colorHex: "#00ff00", order: 2 }),
    ];

    render(
      <EditorLayersPanel
        layers={layers}
        visibility={{ "group-a": true, "group-b": true, "group-c": true }}
        onToggleVisibility={vi.fn()}
        onToggleLocked={vi.fn()}
        onReorder={onReorder}
        selectedGroupId={null}
        onSelectGroup={vi.fn()}
      />,
    );

    const rows = screen.getAllByRole("listitem");
    expect(rows).toHaveLength(3);

    const dataTransfer = dataTransferStub();
    fireEvent.dragStart(rows[0], { dataTransfer });
    fireEvent.dragOver(rows[2], { dataTransfer });
    fireEvent.drop(rows[2], { dataTransfer });

    expect(onReorder).toHaveBeenCalledTimes(1);
    // Soltar "Rojo" (group-a) sobre "Verde" (group-c) lo inserta inmediatamente
    // antes de "Verde": el nuevo orden completo es [Azul, Rojo, Verde].
    expect(onReorder).toHaveBeenCalledWith(["group-b", "group-a", "group-c"]);
  });

  it("soltar una fila sobre sí misma NO llama a onReorder", () => {
    const onReorder = vi.fn();
    render(
      <EditorLayersPanel
        layers={[layer({ groupId: "group-a", order: 0 }), layer({ groupId: "group-b", name: "Azul", order: 1 })]}
        visibility={{ "group-a": true, "group-b": true }}
        onToggleVisibility={vi.fn()}
        onToggleLocked={vi.fn()}
        onReorder={onReorder}
        selectedGroupId={null}
        onSelectGroup={vi.fn()}
      />,
    );

    const rows = screen.getAllByRole("listitem");
    const dataTransfer = dataTransferStub();
    fireEvent.dragStart(rows[0], { dataTransfer });
    fireEvent.drop(rows[0], { dataTransfer });

    expect(onReorder).not.toHaveBeenCalled();
  });
});
