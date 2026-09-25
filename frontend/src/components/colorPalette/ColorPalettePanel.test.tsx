import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { ColorPalettePanel } from "./ColorPalettePanel";

const PROJECT_ID = "11111111-1111-1111-1111-111111111111";
const IMAGE_ID = "22222222-2222-2222-2222-222222222222";
const PALETTE_ID = "33333333-3333-3333-3333-333333333333";
const GROUP_A_ID = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
const GROUP_B_ID = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
const MERGED_GROUP_ID = "cccccccc-cccc-cccc-cccc-cccccccccccc";

function group(overrides: Record<string, unknown> = {}) {
  return {
    groupId: GROUP_A_ID,
    name: "Color 1",
    colorHex: "#ff0000",
    pixelCount: 100,
    areaPercent: 50.0,
    hasPartialAlpha: false,
    maskUrl: `/api/v1/projects/${PROJECT_ID}/images/${IMAGE_ID}/color-palette/${PALETTE_ID}/groups/${GROUP_A_ID}/mask`,
    isMerged: false,
    ...overrides,
  };
}

function paletteResponse(overrides: Record<string, unknown> = {}): Response {
  return new Response(
    JSON.stringify({
      projectId: PROJECT_ID,
      imageId: IMAGE_ID,
      paletteId: PALETTE_ID,
      version: 1,
      tolerance: 12,
      maxColors: null,
      sourceWidthPx: 10,
      sourceHeightPx: 10,
      transparentPercent: 0,
      groups: [
        group(),
        group({ groupId: GROUP_B_ID, name: "Color 2", colorHex: "#00ff00", areaPercent: 50.0 }),
      ],
      previewUrl: `/api/v1/projects/${PROJECT_ID}/images/${IMAGE_ID}/color-palette/${PALETTE_ID}/preview`,
      isConfirmed: false,
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

function renderPanel() {
  return render(
    <ColorPalettePanel
      projectId={PROJECT_ID}
      imageId={IMAGE_ID}
      fileName="logo.png"
      originalUrl={`/api/v1/projects/${PROJECT_ID}/images/${IMAGE_ID}/original`}
      originalWidth={10}
      originalHeight={10}
    />,
  );
}

describe("ColorPalettePanel — estado inicial", () => {
  it("no pide nada al montar y muestra el botón de detección", () => {
    const fetch = vi.fn();
    vi.stubGlobal("fetch", fetch);

    renderPanel();

    expect(screen.getByRole("button", { name: "Detectar paleta" })).toBeInTheDocument();
    expect(fetch).not.toHaveBeenCalled();
    expect(screen.queryByText("Colores detectados")).not.toBeInTheDocument();
  });
});

describe("ColorPalettePanel — detección", () => {
  it("al pulsar 'Detectar paleta' pide la detección y muestra los grupos y el preview", async () => {
    const fetch = vi.fn((_input: RequestInfo | URL, _init?: RequestInit) => Promise.resolve(paletteResponse()));
    vi.stubGlobal("fetch", fetch);

    renderPanel();
    fireEvent.click(screen.getByRole("button", { name: "Detectar paleta" }));

    expect(await screen.findByText("Colores detectados")).toBeInTheDocument();
    expect(screen.getByText("2")).toBeInTheDocument();
    expect(screen.getByDisplayValue("Color 1")).toBeInTheDocument();
    expect(screen.getByDisplayValue("Color 2")).toBeInTheDocument();
    expect(screen.getAllByText("50.0%")).toHaveLength(2);

    expect(fetch).toHaveBeenCalledWith(
      expect.stringMatching(new RegExp(`/api/v1/projects/${PROJECT_ID}/images/${IMAGE_ID}/color-palette/detect$`)),
      expect.objectContaining({ method: "POST" }),
    );
    const init = fetch.mock.calls[0][1] as RequestInit;
    expect(JSON.parse(init.body as string)).toEqual({ paletteId: null, tolerance: 12, maxColors: null });
  });

  it("un error controlado de la Web API se muestra sin romper el panel", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(() => Promise.resolve(errorResponse(404, "not_found", "No existe un proyecto/imagen con esos IDs."))),
    );

    renderPanel();
    fireEvent.click(screen.getByRole("button", { name: "Detectar paleta" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("No existe un proyecto/imagen con esos IDs.");
    expect(screen.getByRole("button", { name: "Detectar paleta" })).toBeEnabled();
  });
});

describe("ColorPalettePanel — fusionar/deshacer fusión", () => {
  it("seleccionar 2 grupos y fusionar llama a merge y actualiza el preview con un único grupo", async () => {
    const fetch = vi.fn((input: RequestInfo | URL) => {
      const url = input.toString();
      if (url.includes("/merge")) {
        return Promise.resolve(
          paletteResponse({
            version: 2,
            groups: [group({ groupId: MERGED_GROUP_ID, name: "Color 1 + Color 2", areaPercent: 100.0, isMerged: true })],
          }),
        );
      }
      return Promise.resolve(paletteResponse());
    });
    vi.stubGlobal("fetch", fetch);

    renderPanel();
    fireEvent.click(screen.getByRole("button", { name: "Detectar paleta" }));
    await screen.findByText("Colores detectados");

    fireEvent.click(screen.getByRole("checkbox", { name: "Seleccionar Color 1 para fusionar" }));
    fireEvent.click(screen.getByRole("checkbox", { name: "Seleccionar Color 2 para fusionar" }));
    fireEvent.click(screen.getByRole("button", { name: /Fusionar seleccionados/ }));

    expect(await screen.findByDisplayValue("Color 1 + Color 2")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Deshacer fusión" })).toBeInTheDocument();
    expect(fetch).toHaveBeenCalledWith(
      expect.stringMatching(new RegExp(`/color-palette/${PALETTE_ID}/merge$`)),
      expect.objectContaining({ method: "POST" }),
    );
  });

  it("'Fusionar seleccionados' está deshabilitado con menos de 2 grupos seleccionados", async () => {
    vi.stubGlobal("fetch", vi.fn(() => Promise.resolve(paletteResponse())));

    renderPanel();
    fireEvent.click(screen.getByRole("button", { name: "Detectar paleta" }));
    await screen.findByText("Colores detectados");

    expect(screen.getByRole("button", { name: /Fusionar seleccionados/ })).toBeDisabled();

    fireEvent.click(screen.getByRole("checkbox", { name: "Seleccionar Color 1 para fusionar" }));
    expect(screen.getByRole("button", { name: /Fusionar seleccionados/ })).toBeDisabled();
  });

  it("deshacer fusión llama a unmerge y restaura los grupos originales", async () => {
    const fetch = vi.fn((input: RequestInfo | URL) => {
      const url = input.toString();
      if (url.includes("/unmerge")) {
        return Promise.resolve(paletteResponse({ version: 3 }));
      }
      return Promise.resolve(
        paletteResponse({
          version: 2,
          groups: [group({ groupId: MERGED_GROUP_ID, name: "Fusión", areaPercent: 100.0, isMerged: true })],
        }),
      );
    });
    vi.stubGlobal("fetch", fetch);

    renderPanel();
    fireEvent.click(screen.getByRole("button", { name: "Detectar paleta" }));
    await screen.findByDisplayValue("Fusión");

    fireEvent.click(screen.getByRole("button", { name: "Deshacer fusión" }));

    expect(await screen.findByDisplayValue("Color 1")).toBeInTheDocument();
    expect(screen.getByDisplayValue("Color 2")).toBeInTheDocument();
    expect(fetch).toHaveBeenCalledWith(
      expect.stringMatching(new RegExp(`/color-palette/${PALETTE_ID}/unmerge$`)),
      expect.objectContaining({ method: "POST" }),
    );
  });
});

describe("ColorPalettePanel — renombrar", () => {
  it("editar el nombre de un grupo y salir del campo llama a rename", async () => {
    const fetch = vi.fn((input: RequestInfo | URL, _init?: RequestInit) => {
      const url = input.toString();
      if (url.includes("/rename")) {
        return Promise.resolve(paletteResponse({ version: 2, groups: [group({ name: "Rojo principal" }), group({ groupId: GROUP_B_ID, name: "Color 2" })] }));
      }
      return Promise.resolve(paletteResponse());
    });
    vi.stubGlobal("fetch", fetch);

    renderPanel();
    fireEvent.click(screen.getByRole("button", { name: "Detectar paleta" }));
    await screen.findByText("Colores detectados");

    const nameInput = screen.getByDisplayValue("Color 1");
    fireEvent.change(nameInput, { target: { value: "Rojo principal" } });
    fireEvent.blur(nameInput);

    expect(await screen.findByDisplayValue("Rojo principal")).toBeInTheDocument();
    const init = fetch.mock.calls[fetch.mock.calls.length - 1][1] as RequestInit;
    expect(JSON.parse(init.body as string)).toEqual({ groupId: GROUP_A_ID, name: "Rojo principal" });
  });
});

describe("ColorPalettePanel — confirmar", () => {
  it("confirmar llama a confirm y deshabilita la edición", async () => {
    const fetch = vi.fn((input: RequestInfo | URL) => {
      const url = input.toString();
      if (url.includes("/confirm")) {
        return Promise.resolve(paletteResponse({ version: 2, isConfirmed: true }));
      }
      return Promise.resolve(paletteResponse());
    });
    vi.stubGlobal("fetch", fetch);

    renderPanel();
    fireEvent.click(screen.getByRole("button", { name: "Detectar paleta" }));
    await screen.findByText("Colores detectados");

    fireEvent.click(screen.getByRole("button", { name: "Confirmar paleta" }));

    await waitFor(() => expect(screen.getByText("Confirmada")).toBeInTheDocument());
    expect(screen.getByRole("checkbox", { name: "Seleccionar Color 1 para fusionar" })).toBeDisabled();
    expect(fetch).toHaveBeenCalledWith(
      expect.stringMatching(new RegExp(`/color-palette/${PALETTE_ID}/confirm$`)),
      expect.objectContaining({ method: "POST" }),
    );
  });
});
