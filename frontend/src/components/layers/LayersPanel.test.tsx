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

describe("LayersPanel — color real de las capas (regresión M2.1-S01: 'termina en blanco y negro')", () => {
  // Causa raíz confirmada de M2.1-S01: el SVG real de cada capa (servido por
  // GET .../vectors/{vectorId}, el mismo que consume el <img> del canvas
  // combinado) llegaba SIEMPRE con fill="#000000", sin importar el colorHex
  // real del grupo -- la metadata (usada por LayerList para los swatches) se
  // veía bien, pero el canvas combinado se veía en blanco y negro. Esta
  // prueba fija el contrato del lado del cliente: (a) cada capa apunta a una
  // URL de SVG DISTINTA (una por vectorId, nunca la misma para dos colores
  // distintos) y (b) el contenido real que esa URL serviría (simulado acá
  // como YA CORREGIDO, ver VectorLayerServiceTests/SvgFillWriterTests para
  // la corrección del lado del servidor) tiene un fill distinto por capa y
  // NUNCA "#000000" -- si el backend volviera a devolver negro fijo, esta
  // prueba de contrato seguiría pasando (no reemplaza los tests de backend),
  // pero documenta y fija el comportamiento esperado end-to-end.
  function svgWithFill(colorHex: string): string {
    return `<svg xmlns="http://www.w3.org/2000/svg" width="10" height="10"><path d="M2,2 L8,2 L8,8 L2,8 Z" fill="${colorHex}" /></svg>`;
  }

  function fetchMockRespondingWithRealColors() {
    return vi.fn((input: RequestInfo | URL) => {
      const url = typeof input === "string" ? input : input.toString();

      if (url.includes(`/vectors/${VECTOR_A_ID}`)) {
        return Promise.resolve(
          new Response(svgWithFill("#ff0000"), { status: 200, headers: { "Content-Type": "image/svg+xml" } }),
        );
      }
      if (url.includes(`/vectors/${VECTOR_B_ID}`)) {
        return Promise.resolve(
          new Response(svgWithFill("#00ff00"), { status: 200, headers: { "Content-Type": "image/svg+xml" } }),
        );
      }
      if (url.endsWith("/layers")) {
        return Promise.resolve(layerSetResponse());
      }

      throw new Error(`URL no mockeada en este test: ${url}`);
    });
  }

  it("cada capa del canvas combinado apunta a una URL de SVG distinta (una por color/vectorId)", async () => {
    vi.stubGlobal("fetch", fetchMockRespondingWithRealColors());

    renderPanel();
    fireEvent.click(screen.getByRole("button", { name: "Generar capas" }));

    const imgA = await screen.findByRole("img", { name: "Capa Color 1" });
    const imgB = await screen.findByRole("img", { name: "Capa Color 2" });

    const srcA = imgA.getAttribute("src");
    const srcB = imgB.getAttribute("src");

    expect(srcA).toContain(VECTOR_A_ID);
    expect(srcB).toContain(VECTOR_B_ID);
    expect(srcA).not.toEqual(srcB);
  });

  it("el SVG real detrás de cada <img> tiene el fill de su propio color, distinto entre capas y nunca negro", async () => {
    vi.stubGlobal("fetch", fetchMockRespondingWithRealColors());

    renderPanel();
    fireEvent.click(screen.getByRole("button", { name: "Generar capas" }));

    const imgA = await screen.findByRole("img", { name: "Capa Color 1" });
    const imgB = await screen.findByRole("img", { name: "Capa Color 2" });

    const svgTextA = await (await fetch(imgA.getAttribute("src")!)).text();
    const svgTextB = await (await fetch(imgB.getAttribute("src")!)).text();

    const fillOf = (svg: string) => svg.match(/fill="([^"]+)"/)?.[1];

    expect(fillOf(svgTextA)).toBe("#ff0000");
    expect(fillOf(svgTextB)).toBe("#00ff00");
    expect(fillOf(svgTextA)).not.toBe("#000000");
    expect(fillOf(svgTextB)).not.toBe("#000000");
    expect(fillOf(svgTextA)).not.toBe(fillOf(svgTextB));
  });
});
