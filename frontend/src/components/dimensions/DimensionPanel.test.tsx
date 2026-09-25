import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { DimensionPanel, type DimensionSourceOption } from "./DimensionPanel";

const PROJECT_ID = "11111111-1111-1111-1111-111111111111";
const IMAGE_ID = "22222222-2222-2222-2222-222222222222";
const VECTOR_ID = "33333333-3333-3333-3333-333333333333";
const SIMPLIFICATION_ID = "44444444-4444-4444-4444-444444444444";
const DIMENSION_ID = "55555555-5555-5555-5555-555555555555";

const VECTOR_SOURCE: DimensionSourceOption = {
  kind: "vector",
  id: VECTOR_ID,
  label: "Vector actual",
  widthPx: 10,
  heightPx: 10,
};

const SIMPLIFICATION_SOURCE: DimensionSourceOption = {
  kind: "simplification",
  id: SIMPLIFICATION_ID,
  label: "Última simplificación",
  widthPx: 10,
  heightPx: 10,
};

function applyResponse(overrides: Record<string, unknown> = {}): Response {
  return new Response(
    JSON.stringify({
      projectId: PROJECT_ID,
      imageId: IMAGE_ID,
      dimensionId: DIMENSION_ID,
      svgUrl: `/api/v1/projects/${PROJECT_ID}/images/${IMAGE_ID}/dimensions/${DIMENSION_ID}`,
      sourceKind: "vector",
      sourceId: VECTOR_ID,
      version: 1,
      widthMm: 100,
      heightMm: 100,
      lockAspectRatio: true,
      sourceWidthPx: 10,
      sourceHeightPx: 10,
      cached: false,
      ...overrides,
    }),
    { status: 201, headers: { "Content-Type": "application/json" } },
  );
}

function errorResponse(status: number, code: string, message: string): Response {
  return new Response(JSON.stringify({ code, message }), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

function renderPanel(sources: DimensionSourceOption[] = [VECTOR_SOURCE]) {
  return render(<DimensionPanel projectId={PROJECT_ID} imageId={IMAGE_ID} sources={sources} />);
}

describe("DimensionPanel — estado inicial", () => {
  it("no pide nada al montar: proporción bloqueada por defecto y Aplicar deshabilitado", () => {
    const fetch = vi.fn();
    vi.stubGlobal("fetch", fetch);

    renderPanel();

    expect(screen.getByRole("checkbox", { name: "Proporción bloqueada" })).toBeChecked();
    expect(screen.getByRole("button", { name: "Aplicar" })).toBeDisabled();
    expect(fetch).not.toHaveBeenCalled();
  });
});

describe("DimensionPanel — preview local (sin round-trip HTTP)", () => {
  it("completar el ancho calcula el alto para una fuente cuadrada, sin llamar a la Web API", () => {
    const fetch = vi.fn();
    vi.stubGlobal("fetch", fetch);

    renderPanel();
    fireEvent.change(screen.getByLabelText("Ancho (mm)"), { target: { value: "100" } });

    expect(screen.getByText("Tamaño final: 100mm × 100mm.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Aplicar" })).toBeEnabled();
    expect(fetch).not.toHaveBeenCalled();
  });

  it("con la proporción bloqueada, completar el alto limpia el ancho (mutuamente excluyentes)", () => {
    vi.stubGlobal("fetch", vi.fn());

    renderPanel();
    const widthInput = screen.getByLabelText("Ancho (mm)") as HTMLInputElement;
    const heightInput = screen.getByLabelText("Alto (mm)") as HTMLInputElement;

    fireEvent.change(widthInput, { target: { value: "100" } });
    expect(widthInput.value).toBe("100");

    fireEvent.change(heightInput, { target: { value: "50" } });
    expect(heightInput.value).toBe("50");
    expect(widthInput.value).toBe("");
  });

  it("desbloquear la proporción permite completar ambos valores de forma independiente", () => {
    vi.stubGlobal("fetch", vi.fn());

    renderPanel();
    fireEvent.click(screen.getByRole("checkbox", { name: "Proporción bloqueada" }));

    const widthInput = screen.getByLabelText("Ancho (mm)") as HTMLInputElement;
    const heightInput = screen.getByLabelText("Alto (mm)") as HTMLInputElement;

    fireEvent.change(widthInput, { target: { value: "300" } });
    fireEvent.change(heightInput, { target: { value: "20" } });

    expect(widthInput.value).toBe("300");
    expect(heightInput.value).toBe("20");
    expect(screen.getByText("Tamaño final: 300mm × 20mm.")).toBeInTheDocument();
  });
});

describe("DimensionPanel — aplicar", () => {
  it("Aplicar persiste las dimensiones con el valor tal como lo completó el usuario (no el derivado)", async () => {
    const fetch = vi.fn((_input: RequestInfo | URL, _init?: RequestInit) => Promise.resolve(applyResponse()));
    vi.stubGlobal("fetch", fetch);

    renderPanel();
    fireEvent.change(screen.getByLabelText("Ancho (mm)"), { target: { value: "100" } });
    fireEvent.click(screen.getByRole("button", { name: "Aplicar" }));

    expect(await screen.findByText(/Dimensiones aplicadas como versión 1/)).toBeInTheDocument();

    expect(fetch).toHaveBeenCalledWith(
      expect.stringMatching(new RegExp(`/api/v1/projects/${PROJECT_ID}/images/${IMAGE_ID}/dimensions/apply$`)),
      expect.objectContaining({ method: "POST" }),
    );
    const init = fetch.mock.calls[0][1] as RequestInit;
    expect(JSON.parse(init.body as string)).toEqual({
      sourceKind: "vector",
      sourceId: VECTOR_ID,
      widthMm: 100,
      heightMm: null,
      lockAspectRatio: true,
    });
  });

  it("muestra un link al SVG con dimensiones físicas tras aplicar", async () => {
    vi.stubGlobal("fetch", vi.fn(() => Promise.resolve(applyResponse())));

    renderPanel();
    fireEvent.change(screen.getByLabelText("Ancho (mm)"), { target: { value: "100" } });
    fireEvent.click(screen.getByRole("button", { name: "Aplicar" }));

    const link = await screen.findByRole("link", { name: "Ver SVG con dimensiones físicas" });
    expect(link.getAttribute("href")).toContain(
      `/api/v1/projects/${PROJECT_ID}/images/${IMAGE_ID}/dimensions/${DIMENSION_ID}`,
    );
  });
});

describe("DimensionPanel — errores controlados", () => {
  it("un error controlado de la Web API se muestra sin romper el panel", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(() => Promise.resolve(errorResponse(404, "not_found", "No existe un SVG con ese ID."))),
    );

    renderPanel();
    fireEvent.change(screen.getByLabelText("Ancho (mm)"), { target: { value: "100" } });
    fireEvent.click(screen.getByRole("button", { name: "Aplicar" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("No existe un SVG con ese ID.");
    await waitFor(() => expect(screen.getByRole("button", { name: "Aplicar" })).toBeEnabled());
  });
});

describe("DimensionPanel — selección de fuente", () => {
  it("muestra el selector de fuente solo si hay una simplificación disponible", () => {
    vi.stubGlobal("fetch", vi.fn());
    renderPanel();

    expect(screen.queryByRole("radiogroup", { name: "Fuente a dimensionar" })).not.toBeInTheDocument();
  });

  it("con una simplificación disponible, cambiar de fuente reinicia el resultado aplicado", async () => {
    vi.stubGlobal("fetch", vi.fn(() => Promise.resolve(applyResponse())));
    renderPanel([VECTOR_SOURCE, SIMPLIFICATION_SOURCE]);

    // Por defecto se selecciona la fuente más refinada (la última: la simplificación).
    expect(screen.getByRole("radio", { name: "Última simplificación" })).toBeChecked();

    fireEvent.change(screen.getByLabelText("Ancho (mm)"), { target: { value: "100" } });
    fireEvent.click(screen.getByRole("button", { name: "Aplicar" }));
    await screen.findByText(/Dimensiones aplicadas como versión 1/);

    fireEvent.click(screen.getByRole("radio", { name: "Vector actual" }));

    expect(screen.queryByText(/Dimensiones aplicadas como versión 1/)).not.toBeInTheDocument();
  });
});
