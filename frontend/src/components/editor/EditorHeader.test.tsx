import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { EditorHeader } from "./EditorHeader";

describe("EditorHeader", () => {
  it("muestra el nombre del proyecto y un estado honesto (no un 'Saved' falso)", () => {
    render(<EditorHeader projectName="mi-diseño.svg" onClose={vi.fn()} />);
    expect(screen.getByText("mi-diseño.svg")).toBeInTheDocument();
    expect(screen.queryByText(/^Saved$/)).not.toBeInTheDocument();
    expect(screen.queryByText("✓")).not.toBeInTheDocument();
    expect(screen.getByText(/Sin guardado automático todavía/)).toBeInTheDocument();
  });

  it("← Projects llama a onClose", () => {
    const onClose = vi.fn();
    render(<EditorHeader projectName="mi-diseño.svg" onClose={onClose} />);
    fireEvent.click(screen.getByRole("button", { name: /Volver a la lista de proyectos/ }));
    expect(onClose).toHaveBeenCalled();
  });

  it("Undo/Redo/Save están presentes pero deshabilitados", () => {
    render(<EditorHeader projectName="mi-diseño.svg" onClose={vi.fn()} />);
    expect(screen.getByRole("button", { name: /Deshacer/ })).toBeDisabled();
    expect(screen.getByRole("button", { name: /Rehacer/ })).toBeDisabled();
    expect(screen.getByRole("button", { name: /Guardar/ })).toBeDisabled();
    expect(screen.getByRole("button", { name: /Exportar/ })).toBeDisabled();
  });
});
