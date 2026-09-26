import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { LayersPanel } from "./LayersPanel";

/**
 * Pruebas de operación de fabricación por color (M2-S07): selector por capa
 * (Corte/Grabado/Ignorar), leyenda visual, filtro ("mostrar solo Corte") y
 * resumen ("N en Corte, M en Grabado, K ignoradas, J sin asignar"). Mismo
 * criterio de fixtures/estilo que ComponentGroups.test.tsx: fetch fake que
 * dispatcha por URL+método.
 */

const PROJECT_ID = "11111111-1111-1111-1111-111111111111";
const IMAGE_ID = "22222222-2222-2222-2222-222222222222";
const PALETTE_ID = "33333333-3333-3333-3333-333333333333";
const GROUP_A_ID = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
const GROUP_B_ID = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
const VECTOR_A_ID = "cccccccc-cccc-cccc-cccc-cccccccccccc";
const VECTOR_B_ID = "dddddddd-dddd-dddd-dddd-dddddddddddd";
const LAYER_SET_ID = "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee";

const LAYERS_URL = new RegExp(`/color-palette/${PALETTE_ID}/layers$`);
const OPERATIONS_URL = new RegExp(`/color-palette/${PALETTE_ID}/layers/operations$`);
const ASSIGN_A_URL = new RegExp(`/color-palette/${PALETTE_ID}/layers/${GROUP_A_ID}/operation$`);
const ASSIGN_B_URL = new RegExp(`/color-palette/${PALETTE_ID}/layers/${GROUP_B_ID}/operation$`);

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

function layerSetResponse(): Response {
  return new Response(
    JSON.stringify({
      projectId: PROJECT_ID,
      imageId: IMAGE_ID,
      layerSetId: LAYER_SET_ID,
      version: 1,
      paletteId: PALETTE_ID,
      paletteVersion: 2,
      sourceWidthPx: 100,
      sourceHeightPx: 100,
      layers: [
        layer(),
        layer({ groupId: GROUP_B_ID, name: "Color 2", colorHex: "#00ff00", vectorId: VECTOR_B_ID, areaPercent: 40.0 }),
      ],
      cached: false,
    }),
    { status: 201, headers: { "Content-Type": "application/json" } },
  );
}

function operationSetResponse(operations: Array<{ groupId: string; name: string; colorHex: string; operation: string }>): Response {
  const summary = {
    cutCount: operations.filter((o) => o.operation === "cut").length,
    engraveCount: operations.filter((o) => o.operation === "engrave").length,
    ignoreCount: operations.filter((o) => o.operation === "ignore").length,
    unassignedCount: operations.filter((o) => o.operation === "unassigned").length,
    totalCount: operations.length,
  };
  return new Response(
    JSON.stringify({
      projectId: PROJECT_ID,
      imageId: IMAGE_ID,
      paletteId: PALETTE_ID,
      paletteVersion: 2,
      layerSetId: LAYER_SET_ID,
      version: operations.some((o) => o.operation !== "unassigned") ? 1 : 0,
      operations,
      summary,
    }),
    { status: 200, headers: { "Content-Type": "application/json" } },
  );
}

function unassignedOperations() {
  return [
    { groupId: GROUP_A_ID, name: "Color 1", colorHex: "#ff0000", operation: "unassigned" },
    { groupId: GROUP_B_ID, name: "Color 2", colorHex: "#00ff00", operation: "unassigned" },
  ];
}

interface FetchRoute {
  test: (url: string, method: string) => boolean;
  respond: (url: string, init?: RequestInit) => Response;
}

function stubFetch(routes: FetchRoute[]) {
  return vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
    const url = typeof input === "string" ? input : input.toString();
    const method = (init?.method ?? "GET").toUpperCase();
    const route = routes.find((candidate) => candidate.test(url, method));
    if (!route) {
      throw new Error(`URL/método no simulado: ${method} ${url}`);
    }
    return Promise.resolve(route.respond(url, init));
  });
}

function renderPanel() {
  return render(<LayersPanel projectId={PROJECT_ID} imageId={IMAGE_ID} paletteId={PALETTE_ID} />);
}

async function setUpWithLayers(extraRoutes: FetchRoute[] = [], getOperationsResponse: () => Response = () => operationSetResponse(unassignedOperations())) {
  const fetch = stubFetch([
    { test: (url) => LAYERS_URL.test(url), respond: () => layerSetResponse() },
    { test: (url, method) => OPERATIONS_URL.test(url) && method === "GET", respond: getOperationsResponse },
    ...extraRoutes,
  ]);
  vi.stubGlobal("fetch", fetch);

  renderPanel();
  fireEvent.click(screen.getByRole("button", { name: "Generar capas" }));
  await screen.findByRole("listitem", { name: "Capa Color 1" });

  return fetch;
}

describe("Operación de fabricación — leyenda y resumen iniciales", () => {
  it("sin ninguna asignación, todas las capas se muestran 'Sin asignar' y el resumen las cuenta como tales", async () => {
    await setUpWithLayers();

    expect(await screen.findByText(/0 en Corte, 0 en Grabado, 0 ignoradas, 2 sin asignar/)).toBeInTheDocument();
    expect(document.querySelectorAll(".manufacturing-operation-badge--unassigned")).toHaveLength(2);
  });
});

describe("Operación de fabricación — asignar", () => {
  it("elegir Corte en el selector de una capa la persiste y actualiza la leyenda/resumen", async () => {
    const fetch = await setUpWithLayers([
      {
        test: (url, method) => ASSIGN_A_URL.test(url) && method === "POST",
        respond: () =>
          operationSetResponse([
            { groupId: GROUP_A_ID, name: "Color 1", colorHex: "#ff0000", operation: "cut" },
            { groupId: GROUP_B_ID, name: "Color 2", colorHex: "#00ff00", operation: "unassigned" },
          ]),
      },
    ]);
    await screen.findByText(/0 en Corte, 0 en Grabado, 0 ignoradas, 2 sin asignar/);

    fireEvent.change(screen.getByRole("combobox", { name: "Operación de fabricación de la capa Color 1" }), {
      target: { value: "cut" },
    });

    expect(await screen.findByText(/1 en Corte, 0 en Grabado, 0 ignoradas, 1 sin asignar/)).toBeInTheDocument();

    expect(fetch).toHaveBeenCalledWith(
      expect.stringMatching(ASSIGN_A_URL),
      expect.objectContaining({ method: "POST", body: JSON.stringify({ operation: "cut" }) }),
    );
  });

  it("combinación Corte + Grabado en el mismo conjunto: el resumen distingue ambas", async () => {
    await setUpWithLayers(
      [
        {
          test: (url, method) => ASSIGN_A_URL.test(url) && method === "POST",
          respond: () =>
            operationSetResponse([
              { groupId: GROUP_A_ID, name: "Color 1", colorHex: "#ff0000", operation: "cut" },
              { groupId: GROUP_B_ID, name: "Color 2", colorHex: "#00ff00", operation: "engrave" },
            ]),
        },
      ],
      () =>
        operationSetResponse([
          { groupId: GROUP_A_ID, name: "Color 1", colorHex: "#ff0000", operation: "unassigned" },
          { groupId: GROUP_B_ID, name: "Color 2", colorHex: "#00ff00", operation: "engrave" },
        ]),
    );

    await screen.findByText(/0 en Corte, 1 en Grabado, 0 ignoradas, 1 sin asignar/);

    fireEvent.change(screen.getByRole("combobox", { name: "Operación de fabricación de la capa Color 1" }), {
      target: { value: "cut" },
    });

    expect(await screen.findByText(/1 en Corte, 1 en Grabado, 0 ignoradas, 0 sin asignar/)).toBeInTheDocument();
  });

  it("marcar una capa como Ignorar se refleja en el resumen y la leyenda", async () => {
    await setUpWithLayers([
      {
        test: (url, method) => ASSIGN_B_URL.test(url) && method === "POST",
        respond: () =>
          operationSetResponse([
            { groupId: GROUP_A_ID, name: "Color 1", colorHex: "#ff0000", operation: "unassigned" },
            { groupId: GROUP_B_ID, name: "Color 2", colorHex: "#00ff00", operation: "ignore" },
          ]),
      },
    ]);

    fireEvent.change(screen.getByRole("combobox", { name: "Operación de fabricación de la capa Color 2" }), {
      target: { value: "ignore" },
    });

    expect(await screen.findByText(/0 en Corte, 0 en Grabado, 1 ignorada, 1 sin asignar/)).toBeInTheDocument();
  });
});

describe("Operación de fabricación — filtro por operación", () => {
  it("filtrar por 'Corte' oculta las capas con otra operación (sin afectar visibilidad/canvas)", async () => {
    await setUpWithLayers([], () =>
      operationSetResponse([
        { groupId: GROUP_A_ID, name: "Color 1", colorHex: "#ff0000", operation: "cut" },
        { groupId: GROUP_B_ID, name: "Color 2", colorHex: "#00ff00", operation: "engrave" },
      ]),
    );
    await screen.findByText(/1 en Corte, 1 en Grabado, 0 ignoradas, 0 sin asignar/);

    fireEvent.click(screen.getByRole("radio", { name: "Mostrar solo Corte" }));

    expect(screen.getByRole("listitem", { name: "Capa Color 1" })).toBeInTheDocument();
    expect(screen.queryByRole("listitem", { name: "Capa Color 2" })).not.toBeInTheDocument();

    // El canvas combinado sigue mostrando AMBAS capas: el filtro es solo de la lista.
    expect(screen.getByRole("img", { name: "Capa Color 2" })).toBeInTheDocument();

    fireEvent.click(screen.getByRole("radio", { name: "Mostrar todas las capas" }));
    expect(screen.getByRole("listitem", { name: "Capa Color 2" })).toBeInTheDocument();
  });
});
