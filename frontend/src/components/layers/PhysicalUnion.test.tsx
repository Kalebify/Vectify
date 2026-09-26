import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { LayersPanel } from "./LayersPanel";

/**
 * Pruebas de unión física de piezas (M2-S06): preview antes/después
 * (geometría real, sin persistir nada), confirmar (persiste una
 * VectorVersion nueva, la capa pasa a mostrar el resultado fusionado) y
 * cancelar (sin ningún llamado a la Web API). También cubre que la unión
 * geométricamente imposible explica por qué en vez de fingir éxito. Mismo
 * criterio de fixtures/estilo que ComponentGroups.test.tsx.
 */

const PROJECT_ID = "11111111-1111-1111-1111-111111111111";
const IMAGE_ID = "22222222-2222-2222-2222-222222222222";
const PALETTE_ID = "33333333-3333-3333-3333-333333333333";
const GROUP_A_ID = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
const VECTOR_A_ID = "cccccccc-cccc-cccc-cccc-cccccccccccc";
const VECTOR_A2_ID = "ffffffff-ffff-ffff-ffff-ffffffffffff";

const LAYERS_URL = new RegExp(`/color-palette/${PALETTE_ID}/layers$`);
const COMPONENTS_A_URL = new RegExp(`/vectors/${VECTOR_A_ID}/components$`);
const COMPONENTS_A2_URL = new RegExp(`/vectors/${VECTOR_A2_ID}/components$`);
const COMPONENT_GROUPS_A_URL = new RegExp(`/vectors/${VECTOR_A_ID}/components/groups$`);
const COMPONENT_GROUPS_A2_URL = new RegExp(`/vectors/${VECTOR_A2_ID}/components/groups$`);
const UNION_PREVIEW_A_URL = new RegExp(`/vectors/${VECTOR_A_ID}/physical-union/preview$`);
const UNION_CONFIRM_A_URL = new RegExp(`/vectors/${VECTOR_A_ID}/physical-union/confirm$`);

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
      layerSetId: "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee",
      version: 1,
      paletteId: PALETTE_ID,
      paletteVersion: 2,
      sourceWidthPx: 100,
      sourceHeightPx: 100,
      layers: [layer()],
      cached: false,
    }),
    { status: 201, headers: { "Content-Type": "application/json" } },
  );
}

function componentsResponse(vectorId: string, components: unknown[]): Response {
  return new Response(
    JSON.stringify({
      projectId: PROJECT_ID,
      imageId: IMAGE_ID,
      componentSetId: `component-set-${vectorId}`,
      version: 1,
      vectorId,
      components,
      skippedPathCount: 0,
      cached: false,
    }),
    { status: 201, headers: { "Content-Type": "application/json" } },
  );
}

function groupSetResponse(vectorId: string): Response {
  return new Response(
    JSON.stringify({ projectId: PROJECT_ID, imageId: IMAGE_ID, vectorId, version: 0, groups: [] }),
    { status: 200, headers: { "Content-Type": "application/json" } },
  );
}

function errorResponse(status: number, code: string, message: string): Response {
  return new Response(JSON.stringify({ code, message }), { status, headers: { "Content-Type": "application/json" } });
}

const TWO_PIECE_COMPONENTS_A = [
  {
    id: "component-1",
    members: [{ pathIndex: 0, subpathIndex: 0, role: "solid", bounds: { minX: 0, minY: 0, maxX: 20, maxY: 20 }, area: 400 }],
    bounds: { minX: 0, minY: 0, maxX: 20, maxY: 20 },
    area: 400,
    isTiny: false,
  },
  {
    id: "component-2",
    members: [{ pathIndex: 1, subpathIndex: 0, role: "solid", bounds: { minX: 70, minY: 70, maxX: 90, maxY: 90 }, area: 400 }],
    bounds: { minX: 70, minY: 70, maxX: 90, maxY: 90 },
    area: 400,
    isTiny: false,
  },
];

const ONE_PIECE_COMPONENTS_A2 = [
  {
    id: "component-1",
    members: [
      { pathIndex: 0, subpathIndex: 0, role: "solid", bounds: { minX: 0, minY: 0, maxX: 90, maxY: 90 }, area: 900 },
    ],
    bounds: { minX: 0, minY: 0, maxX: 90, maxY: 90 },
    area: 900,
    isTiny: false,
  },
];

function previewResponse(componentCountAfter: number, strategy = "bridge"): Response {
  return new Response(
    JSON.stringify({
      projectId: PROJECT_ID,
      imageId: IMAGE_ID,
      vectorId: VECTOR_A_ID,
      componentIds: ["component-1", "component-2"],
      svg: '<svg xmlns="http://www.w3.org/2000/svg" width="100" height="100"><path d="M0,0 L90,0 L90,90 L0,90 Z" fill="#000000"/></svg>',
      contentType: "image/svg+xml",
      width: 100,
      height: 100,
      metrics: { pathCount: 1, approxNodeCount: 4, bounds: { minX: 0, minY: 0, maxX: 90, maxY: 90, width: 90, height: 90 } },
      componentCountBefore: 2,
      componentCountAfter,
      strategy,
      bridgeCount: 1,
    }),
    { status: 200, headers: { "Content-Type": "application/json" } },
  );
}

function confirmResponse(): Response {
  return new Response(
    JSON.stringify({
      projectId: PROJECT_ID,
      imageId: IMAGE_ID,
      previousVectorId: VECTOR_A_ID,
      newVectorId: VECTOR_A2_ID,
      svgUrl: `/api/v1/projects/${PROJECT_ID}/images/${IMAGE_ID}/vectors/${VECTOR_A2_ID}`,
      newVectorVersion: 2,
      physicalUnionVersion: 1,
      componentIds: ["component-1", "component-2"],
      width: 100,
      height: 100,
      metrics: { pathCount: 1, approxNodeCount: 4, bounds: { minX: 0, minY: 0, maxX: 90, maxY: 90, width: 90, height: 90 } },
      componentCountBefore: 2,
      componentCountAfter: 1,
      strategy: "bridge",
      bridgeCount: 1,
    }),
    { status: 201, headers: { "Content-Type": "application/json" } },
  );
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

function baseRoutes(): FetchRoute[] {
  return [
    { test: (url) => LAYERS_URL.test(url), respond: () => layerSetResponse() },
    { test: (url, method) => COMPONENTS_A_URL.test(url) && method === "POST", respond: () => componentsResponse(VECTOR_A_ID, TWO_PIECE_COMPONENTS_A) },
    { test: (url, method) => COMPONENT_GROUPS_A_URL.test(url) && method === "GET", respond: () => groupSetResponse(VECTOR_A_ID) },
  ];
}

async function setUpWithTwoSelectedPieces(extraRoutes: FetchRoute[] = []) {
  const fetch = stubFetch([...baseRoutes(), ...extraRoutes]);
  vi.stubGlobal("fetch", fetch);

  render(<LayersPanel projectId={PROJECT_ID} imageId={IMAGE_ID} paletteId={PALETTE_ID} />);
  fireEvent.click(screen.getByRole("button", { name: "Generar capas" }));
  await screen.findByRole("listitem", { name: "Capa Color 1" });

  fireEvent.click(screen.getByRole("button", { name: "Calcular componentes" }));
  await screen.findByText("Color 1: 2 piezas");

  fireEvent.click(screen.getByRole("checkbox", { name: "Seleccionar Pieza 1 de Color 1 para agrupar" }));
  fireEvent.click(screen.getByRole("checkbox", { name: "Seleccionar Pieza 2 de Color 1 para agrupar" }));

  return fetch;
}

describe("Unión física -- distinta de Agrupar", () => {
  it("con 2+ piezas seleccionadas aparecen AMBOS botones, con textos distintos", async () => {
    await setUpWithTwoSelectedPieces();

    expect(screen.getByRole("button", { name: "Agrupar 2 piezas seleccionadas" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Unir físicamente 2 piezas seleccionadas" })).toBeInTheDocument();
  });
});

describe("Unión física -- preview antes/después", () => {
  it("pedir preview muestra conteo antes/después y no persiste nada", async () => {
    const fetch = await setUpWithTwoSelectedPieces([
      { test: (url, method) => UNION_PREVIEW_A_URL.test(url) && method === "POST", respond: () => previewResponse(1) },
    ]);

    fireEvent.click(screen.getByRole("button", { name: "Unir físicamente 2 piezas seleccionadas" }));

    expect(await screen.findByText("2 pieza(s)")).toBeInTheDocument();
    expect(screen.getByText("1 pieza(s)")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Confirmar unión física" })).toBeInTheDocument();

    expect(fetch).toHaveBeenCalledWith(
      expect.stringMatching(UNION_PREVIEW_A_URL),
      expect.objectContaining({ method: "POST", body: JSON.stringify({ componentIds: ["component-1", "component-2"] }) }),
    );
    // Ningún llamado a confirm todavía.
    expect(fetch).not.toHaveBeenCalledWith(expect.stringMatching(UNION_CONFIRM_A_URL), expect.anything());
  });

  it("cancelar el preview no genera ningún llamado adicional a la Web API", async () => {
    const fetch = await setUpWithTwoSelectedPieces([
      { test: (url, method) => UNION_PREVIEW_A_URL.test(url) && method === "POST", respond: () => previewResponse(1) },
    ]);

    fireEvent.click(screen.getByRole("button", { name: "Unir físicamente 2 piezas seleccionadas" }));
    await screen.findByRole("button", { name: "Confirmar unión física" });

    const callCountAfterPreview = fetch.mock.calls.length;
    fireEvent.click(screen.getByRole("button", { name: "Cancelar" }));

    expect(screen.queryByRole("button", { name: "Confirmar unión física" })).not.toBeInTheDocument();
    expect(fetch.mock.calls.length).toBe(callCountAfterPreview);
  });

  it("cuando la unión no es geométricamente posible, explica por qué en vez de fingir éxito", async () => {
    await setUpWithTwoSelectedPieces([
      {
        test: (url, method) => UNION_PREVIEW_A_URL.test(url) && method === "POST",
        respond: () => errorResponse(422, "physical_union_impossible", "Tras la unión, el análisis detectó 2 piezas en vez de 1."),
      },
    ]);

    fireEvent.click(screen.getByRole("button", { name: "Unir físicamente 2 piezas seleccionadas" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/NO fue posible/);
    expect(screen.queryByRole("button", { name: "Confirmar unión física" })).not.toBeInTheDocument();
  });
});

describe("Unión física -- confirmar persiste una VectorVersion nueva", () => {
  it("confirmar reemplaza la capa por el resultado fusionado y recalcula sus componentes", async () => {
    const fetch = await setUpWithTwoSelectedPieces([
      { test: (url, method) => UNION_PREVIEW_A_URL.test(url) && method === "POST", respond: () => previewResponse(1) },
      { test: (url, method) => UNION_CONFIRM_A_URL.test(url) && method === "POST", respond: () => confirmResponse() },
      { test: (url, method) => COMPONENTS_A2_URL.test(url) && method === "POST", respond: () => componentsResponse(VECTOR_A2_ID, ONE_PIECE_COMPONENTS_A2) },
      { test: (url, method) => COMPONENT_GROUPS_A2_URL.test(url) && method === "GET", respond: () => groupSetResponse(VECTOR_A2_ID) },
    ]);

    fireEvent.click(screen.getByRole("button", { name: "Unir físicamente 2 piezas seleccionadas" }));
    await screen.findByRole("button", { name: "Confirmar unión física" });

    fireEvent.click(screen.getByRole("button", { name: "Confirmar unión física" }));

    expect(await screen.findByText("Color 1: 1 pieza")).toBeInTheDocument();
    expect(fetch).toHaveBeenCalledWith(
      expect.stringMatching(UNION_CONFIRM_A_URL),
      expect.objectContaining({ method: "POST", body: JSON.stringify({ componentIds: ["component-1", "component-2"] }) }),
    );

    await waitFor(() => expect(fetch).toHaveBeenCalledWith(expect.stringMatching(COMPONENTS_A2_URL), expect.objectContaining({ method: "POST" })));
  });
});
