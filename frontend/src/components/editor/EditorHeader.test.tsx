import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { EditorHeader } from "./EditorHeader";

function renderHeader(overrides: Partial<Parameters<typeof EditorHeader>[0]> = {}) {
  return render(
    <EditorHeader
      projectName="mi-diseño.svg"
      onClose={vi.fn()}
      saveState="idle"
      saveErrorMessage={null}
      canSave={true}
      onSave={vi.fn()}
      {...overrides}
    />,
  );
}

describe("EditorHeader", () => {
  it("muestra el nombre del proyecto y un estado idle honesto (no un 'Saved' falso sin confirmación)", () => {
    renderHeader({ saveState: "idle" });
    expect(screen.getByText("mi-diseño.svg")).toBeInTheDocument();
    expect(screen.queryByText(/^Guardado$/)).not.toBeInTheDocument();
    expect(screen.queryByText("✓")).not.toBeInTheDocument();
  });

  it("← Projects llama a onClose", () => {
    const onClose = vi.fn();
    renderHeader({ onClose });
    fireEvent.click(screen.getByRole("button", { name: /Volver a la lista de proyectos/ }));
    expect(onClose).toHaveBeenCalled();
  });

  it("Undo/Redo están presentes pero deshabilitados (fuera de alcance de esta tarjeta)", () => {
    renderHeader();
    expect(screen.getByRole("button", { name: /Deshacer/ })).toBeDisabled();
    expect(screen.getByRole("button", { name: /Rehacer/ })).toBeDisabled();
    expect(screen.getByRole("button", { name: /Exportar/ })).toBeDisabled();
  });

  it("el botón Guardar llama a onSave cuando está habilitado", () => {
    const onSave = vi.fn();
    renderHeader({ saveState: "dirty", canSave: true, onSave });
    const button = screen.getByRole("button", { name: /^Guardar$/ });
    expect(button).not.toBeDisabled();
    fireEvent.click(button);
    expect(onSave).toHaveBeenCalled();
  });

  it("deshabilita Guardar mientras el documento no cargó (canSave=false)", () => {
    renderHeader({ canSave: false, saveState: "idle" });
    expect(screen.getByRole("button", { name: /^Guardar$/ })).toBeDisabled();
  });

  it("deshabilita Guardar y muestra 'Guardando…' durante saving", () => {
    renderHeader({ saveState: "saving" });
    expect(screen.getByText("Guardando…")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /^Guardar$/ })).toBeDisabled();
  });

  it("muestra el estado real 'Guardado' SOLO cuando saveState es 'saved' (nunca antes de la confirmación del backend)", () => {
    renderHeader({ saveState: "saved" });
    expect(screen.getByText("Guardado")).toBeInTheDocument();
  });

  it("en estado error muestra el mensaje y permite reintentar sin perder el intento", () => {
    const onSave = vi.fn();
    renderHeader({ saveState: "error", saveErrorMessage: "No se pudo contactar al servidor.", onSave });
    expect(screen.getByText("No se pudo contactar al servidor.")).toBeInTheDocument();
    const retryButton = screen.getByRole("button", { name: /Reintentar guardar/ });
    expect(retryButton).not.toBeDisabled();
    fireEvent.click(retryButton);
    expect(onSave).toHaveBeenCalled();
  });
});
