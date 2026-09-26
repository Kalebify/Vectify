import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { LayersPanel } from "./LayersPanel";

const PROJECT_ID = "11111111-1111-1111-1111-111111111111";
const IMAGE_ID = "22222222-2222-2222-2222-222222222222";
const PALETTE_ID = "33333333-3333-3333-3333-333333333333";
const GROUP_A_ID = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
const GROUP_B_ID = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
const VECTOR_A_ID = "cccccccc-cccc-cccc-cccc-cccccccccccc";
const VECTOR_B_ID = "dddddddd-dddd-dddd-dddd-dddddddddddd";

const LAYERS_URL = new RegExp(`/color-palette/${PALETTE_ID}/layers$`);
const COMPONENTS_A_URL = new RegExp(`/vectors/${VECTOR_A_ID}/components$`);
const COMPONENTS_B_URL = new RegExp(`/vectors/${VECTOR_B_ID}/components$`);

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
      layers: [
        layer(),
        layer({ groupId: GROUP_B_ID, name: "Color 2", colorHex: "#00ff00", vectorId: VECTOR_B_ID, areaPercent: 40.0 }),
      ],
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

function errorResponse(status: number, code: string, message: string): Response {
  return new Response(JSON.stringify({ code, message }), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

const TWO_PIECE_COMPONENTS = [
  {
    id: "component-1",
    members: [{ pathIndex: 0, subpathIndex: 0, role: "solid", bounds: { minX: 0, minY: 0, maxX: 20, maxY: 20 }, area: 400 }],
    bounds: { minX: 0, minY: 0, maxX: 20, maxY: 20 },
    area: 400,
    isTiny: false,
  },
  {
    id: "component-2",
    members: [{ pathIndex: 1, subpathIndex: 0, role: "solid", bounds: { minX: 50, minY: 50, maxX: 70, maxY: 70 }, area: 400 }],
    bounds: { minX: 50, minY: 50, maxX: 70, maxY: 70 },
    area: 400,
    isTiny: false,
  },
];

const ONE_PIECE_COMPONENTS = [
  {
    id: "component-1",
    members: [{ pathIndex: 0, subpathIndex: 0, role: "solid", bounds: { minX: 10, minY: 10, maxX: 30, maxY: 30 }, area: 400 }],
    bounds: { minX: 10, minY: 10, maxX: 30, maxY: 30 },
    area: 400,
    isTiny: false,
  },
];

function renderPanel() {
  return render(<LayersPanel projectId={PROJECT_ID} imageId={IMAGE_ID} paletteId={PALETTE_ID} />);
}

/** fetch fake que dispatcha por URL -- capas, componentes de A, componentes de B. */
function stubFetch(componentsAResponse: () => Response, componentsBResponse: () => Response) {
  return vi.fn((input: RequestInfo | URL) => {
    const url = typeof input === "string" ? input : input.toString();
    if (LAYERS_URL.test(url)) {
      return Promise.resolve(layerSetResponse());
    }
    if (COMPONENTS_A_URL.test(url)) {
      return Promise.resolve(componentsAResponse());
    }
    if (COMPONENTS_B_URL.test(url)) {
      return Promise.resolve(componentsBResponse());
    }
    throw new Error(`URL no simulada: ${url}`);
  });
}

async function generateLayersAndComponents(
  componentsAResponse: () => Response = () => componentsResponse(VECTOR_A_ID, TWO_PIECE_COMPONENTS),
  componentsBResponse: () => Response = () => componentsResponse(VECTOR_B_ID, ONE_PIECE_COMPONENTS),
) {
  const fetch = stubFetch(componentsAResponse, componentsBResponse);
  vi.stubGlobal("fetch", fetch);

  renderPanel();
  fireEvent.click(screen.getByRole("button", { name: "Generar capas" }));
  await screen.findByRole("listitem", { name: "Capa Color 1" });

  fireEvent.click(screen.getByRole("button", { name: "Calcular componentes" }));
  // Siempre espera a "Color 2" (la capa B): en el escenario de error de A,
  // sigue siendo la única que se calcula con éxito -- ver
  // describe("árbol de componentes por capa"), caso "un error controlado...".
  await screen.findByText(/Color 2: 1 pieza/);

  return fetch;
}

describe("LayersPanel — árbol de componentes por capa", () => {
  it("al pulsar 'Calcular componentes' pide el análisis de cada capa y muestra 'Color: N piezas'", async () => {
    const fetch = await generateLayersAndComponents();

    expect(screen.getByText("Color 1: 2 piezas")).toBeInTheDocument();
    expect(await screen.findByText("Color 2: 1 pieza")).toBeInTheDocument();

    expect(fetch).toHaveBeenCalledWith(expect.stringMatching(COMPONENTS_A_URL), expect.objectContaining({ method: "POST" }));
    expect(fetch).toHaveBeenCalledWith(expect.stringMatching(COMPONENTS_B_URL), expect.objectContaining({ method: "POST" }));
  });

  it("un componente diminuto se reporta igual, marcado como tal (nunca se filtra)", async () => {
    await generateLayersAndComponents(() =>
      componentsResponse(VECTOR_A_ID, [
        { ...TWO_PIECE_COMPONENTS[0], area: 0.01, isTiny: true },
        TWO_PIECE_COMPONENTS[1],
      ]),
    );

    expect(screen.getByText("Color 1: 2 piezas")).toBeInTheDocument();
    const list = screen.getByRole("list", { name: "Árbol de capas y sus componentes físicos" });
    expect(within(list).getByRole("button", { name: "Pieza 1 de Color 1 (diminuta)" })).toBeInTheDocument();
  });

  it("un error controlado calculando una capa no rompe el árbol de las que sí se calcularon", async () => {
    await generateLayersAndComponents(
      () => errorResponse(504, "timeout", "El análisis de componentes tardó demasiado."),
      () => componentsResponse(VECTOR_B_ID, ONE_PIECE_COMPONENTS),
    );

    expect(await screen.findByRole("alert")).toHaveTextContent("El análisis de componentes tardó demasiado.");
    expect(await screen.findByText("Color 2: 1 pieza")).toBeInTheDocument();
    expect(screen.queryByText(/Color 1:/)).not.toBeInTheDocument();
  });
});

describe("LayersPanel — selección bidireccional lista/canvas", () => {
  it("seleccionar una pieza en la lista la resalta en el canvas y muestra sus métricas", async () => {
    await generateLayersAndComponents();

    const list = screen.getByRole("list", { name: "Árbol de capas y sus componentes físicos" });
    const canvas = screen.getByRole("group", { name: /Composición combinada/ });

    fireEvent.click(within(list).getByRole("button", { name: "Pieza 1 de Color 1" }));

    expect(within(list).getByRole("button", { name: "Pieza 1 de Color 1" })).toHaveAttribute("aria-pressed", "true");
    expect(within(canvas).getByRole("button", { name: "Pieza 1 de Color 1" })).toHaveAttribute("aria-pressed", "true");

    expect(screen.getByText("Área aproximada")).toBeInTheDocument();
    expect(screen.getByText("400 u²")).toBeInTheDocument();
  });

  it("seleccionar una pieza en el canvas la resalta en la lista", async () => {
    await generateLayersAndComponents();

    const list = screen.getByRole("list", { name: "Árbol de capas y sus componentes físicos" });
    const canvas = screen.getByRole("group", { name: /Composición combinada/ });

    fireEvent.click(within(canvas).getByRole("button", { name: "Pieza 2 de Color 1" }));

    expect(within(list).getByRole("button", { name: "Pieza 2 de Color 1" })).toHaveAttribute("aria-pressed", "true");
    expect(within(canvas).getByRole("button", { name: "Pieza 2 de Color 1" })).toHaveAttribute("aria-pressed", "true");
  });

  it("volver a hacer click sobre la misma pieza la deselecciona", async () => {
    await generateLayersAndComponents();

    const list = screen.getByRole("list", { name: "Árbol de capas y sus componentes físicos" });
    const piece = within(list).getByRole("button", { name: "Pieza 1 de Color 1" });

    fireEvent.click(piece);
    expect(piece).toHaveAttribute("aria-pressed", "true");

    fireEvent.click(piece);
    await waitFor(() => expect(piece).toHaveAttribute("aria-pressed", "false"));
    expect(screen.queryByText("Área aproximada")).not.toBeInTheDocument();
  });
});
