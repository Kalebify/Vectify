import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { LayersPanel } from "./LayersPanel";

const PROJECT_ID = "11111111-1111-1111-1111-111111111111";
const IMAGE_ID = "22222222-2222-2222-2222-222222222222";
const PALETTE_ID = "33333333-3333-3333-3333-333333333333";
const GROUP_A_ID = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
const GROUP_B_ID = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
const VECTOR_A_ID = "cccccccc-cccc-cccc-cccc-cccccccccccc";
const VECTOR_B_ID = "dddddddd-dddd-dddd-dddd-dddddddddddd";

function layer(overrides: Record<string, unknown> = {}) {
  return {
    groupId: GROUP_A_ID,
    name: "Color 1",
    colorHex: "#ff0000",
    areaPercent: 60.0,
    hasPartialAlpha: false,
    vectorId: VECTOR_A_ID,
    svgUrl: `/api/v1/projects/${PROJECT_ID}/images/${IMAGE_ID}/vectors/${VECTOR_A_ID}`,
    ...overrides,
  };
}

function layerSetResponse(overrides: Record<string, unknown> = {}): Response {
  return new Response(
    JSON.stringify({
      projectId: PROJECT_ID,
      imageId: IMAGE_ID,
      layerSetId: "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee",
      version: 1,
      paletteId: PALETTE_ID,
      paletteVersion: 2,
      sourceWidthPx: 10,
      sourceHeightPx: 10,
      layers: [
        layer(),
        layer({ groupId: GROUP_B_ID, name: "Color 2", colorHex: "#00ff00", vectorId: VECTOR_B_ID, areaPercent: 40.0 }),
      ],
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
  return render(<LayersPanel projectId={PROJECT_ID} imageId={IMAGE_ID} paletteId={PALETTE_ID} />);
}

describe("LayersPanel — estado inicial", () => {
  it("no pide nada al montar y muestra el botón de generación", () => {
    const fetch = vi.fn();
    vi.stubGlobal("fetch", fetch);

    renderPanel();

    expect(screen.getByRole("button", { name: "Generar capas" })).toBeInTheDocument();
    expect(fetch).not.toHaveBeenCalled();
  });
});

describe("LayersPanel — generación", () => {
  it("al pulsar 'Generar capas' pide el conjunto completo y muestra una capa por color", async () => {
    const fetch = vi.fn(() => Promise.resolve(layerSetResponse()));
    vi.stubGlobal("fetch", fetch);

    renderPanel();
    fireEvent.click(screen.getByRole("button", { name: "Generar capas" }));

    expect(await screen.findByRole("listitem", { name: "Capa Color 1" })).toBeInTheDocument();
    expect(screen.getByRole("listitem", { name: "Capa Color 2" })).toBeInTheDocument();
    expect(screen.getByText("60.0%")).toBeInTheDocument();
    expect(screen.getByText("40.0%")).toBeInTheDocument();

    expect(fetch).toHaveBeenCalledWith(
      expect.stringMatching(new RegExp(`/color-palette/${PALETTE_ID}/layers$`)),
      expect.objectContaining({ method: "POST" }),
    );
  });

  it("un error controlado (paleta no confirmada) se muestra sin romper el panel", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(() => Promise.resolve(errorResponse(409, "palette_not_confirmed", "La paleta debe estar confirmada."))),
    );

    renderPanel();
    fireEvent.click(screen.getByRole("button", { name: "Generar capas" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("La paleta debe estar confirmada.");
    expect(screen.getByRole("button", { name: "Generar capas" })).toBeEnabled();
  });
});

describe("LayersPanel — visibilidad y renderizado combinado", () => {
  it("todas las capas arrancan visibles: el canvas combinado muestra una imagen por cada una", async () => {
    vi.stubGlobal("fetch", vi.fn(() => Promise.resolve(layerSetResponse())));

    renderPanel();
    fireEvent.click(screen.getByRole("button", { name: "Generar capas" }));
    await screen.findByRole("img", { name: "Capa Color 1" });

    const combined = screen.getByRole("group", { name: /Composición combinada de 2 de 2 capas visibles/ });
    expect(combined.querySelectorAll("img")).toHaveLength(2);
  });

  it("ocultar una capa la saca del canvas combinado sin afectar a las demás", async () => {
    vi.stubGlobal("fetch", vi.fn(() => Promise.resolve(layerSetResponse())));

    renderPanel();
    fireEvent.click(screen.getByRole("button", { name: "Generar capas" }));
    await screen.findByRole("img", { name: "Capa Color 1" });

    fireEvent.click(screen.getByRole("checkbox", { name: "Ocultar la capa Color 1" }));

    const combined = screen.getByRole("group", { name: /Composición combinada de 1 de 2 capas visibles/ });
    expect(combined.querySelectorAll("img")).toHaveLength(1);
    expect(screen.queryByRole("img", { name: "Capa Color 1" })).not.toBeInTheDocument();
    expect(screen.getByRole("img", { name: "Capa Color 2" })).toBeInTheDocument();

    // Volver a mostrarla la reincorpora.
    fireEvent.click(screen.getByRole("checkbox", { name: "Mostrar la capa Color 1" }));
    expect(screen.getByRole("img", { name: "Capa Color 1" })).toBeInTheDocument();
  });

  it("ocultar todas las capas muestra el estado vacío del canvas combinado", async () => {
    vi.stubGlobal("fetch", vi.fn(() => Promise.resolve(layerSetResponse())));

    renderPanel();
    fireEvent.click(screen.getByRole("button", { name: "Generar capas" }));
    await screen.findByRole("img", { name: "Capa Color 1" });

    fireEvent.click(screen.getByRole("checkbox", { name: "Ocultar la capa Color 1" }));
    fireEvent.click(screen.getByRole("checkbox", { name: "Ocultar la capa Color 2" }));

    expect(screen.getByText("Ninguna capa visible. Activá al menos una para verla acá.")).toBeInTheDocument();
  });
});
