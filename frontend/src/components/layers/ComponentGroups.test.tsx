import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { LayersPanel } from "./LayersPanel";

/**
 * Pruebas de agrupación lógica de componentes (M2-S05): multi-select
 * confinado a UNA capa, agrupar/desagrupar/renombrar, "seleccionar como
 * conjunto" (resalta todos los miembros a la vez en el canvas) y manejo sin
 * romper de un grupo "stale" (que referencia un componentId que ya no
 * existe en el análisis vigente). Mismo criterio de fixtures/estilo que
 * LayerComponents.test.tsx: fetch fake que dispatcha por URL+método.
 */

const PROJECT_ID = "11111111-1111-1111-1111-111111111111";
const IMAGE_ID = "22222222-2222-2222-2222-222222222222";
const PALETTE_ID = "33333333-3333-3333-3333-333333333333";
const GROUP_A_ID = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
const GROUP_B_ID = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
const VECTOR_A_ID = "cccccccc-cccc-cccc-cccc-cccccccccccc";
const VECTOR_B_ID = "dddddddd-dddd-dddd-dddd-dddddddddddd";
const COMPONENT_GROUP_ID = "11110000-0000-0000-0000-000000000001";

const LAYERS_URL = new RegExp(`/color-palette/${PALETTE_ID}/layers$`);
const COMPONENTS_A_URL = new RegExp(`/vectors/${VECTOR_A_ID}/components$`);
const COMPONENTS_B_URL = new RegExp(`/vectors/${VECTOR_B_ID}/components$`);
const COMPONENT_GROUPS_A_URL = new RegExp(`/vectors/${VECTOR_A_ID}/components/groups$`);
const COMPONENT_GROUPS_B_URL = new RegExp(`/vectors/${VECTOR_B_ID}/components/groups$`);
const UNGROUP_A_URL = new RegExp(`/vectors/${VECTOR_A_ID}/components/groups/[^/]+/ungroup$`);
const RENAME_A_URL = new RegExp(`/vectors/${VECTOR_A_ID}/components/groups/[^/]+/rename$`);

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

function groupSetResponse(vectorId: string, groups: unknown[], version = 0): Response {
  return new Response(
    JSON.stringify({ projectId: PROJECT_ID, imageId: IMAGE_ID, vectorId, version, groups }),
    { status: 200, headers: { "Content-Type": "application/json" } },
  );
}

const THREE_PIECE_COMPONENTS_A = [
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
  {
    id: "component-3",
    members: [{ pathIndex: 2, subpathIndex: 0, role: "solid", bounds: { minX: 80, minY: 80, maxX: 95, maxY: 95 }, area: 225 }],
    bounds: { minX: 80, minY: 80, maxX: 95, maxY: 95 },
    area: 225,
    isTiny: false,
  },
];

const ONE_PIECE_COMPONENTS_B = [
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

function baseRoutes(getGroupsAResponse: () => Response = () => groupSetResponse(VECTOR_A_ID, [])): FetchRoute[] {
  return [
    { test: (url) => LAYERS_URL.test(url), respond: () => layerSetResponse() },
    { test: (url, method) => COMPONENTS_A_URL.test(url) && method === "POST", respond: () => componentsResponse(VECTOR_A_ID, THREE_PIECE_COMPONENTS_A) },
    { test: (url, method) => COMPONENTS_B_URL.test(url) && method === "POST", respond: () => componentsResponse(VECTOR_B_ID, ONE_PIECE_COMPONENTS_B) },
    { test: (url, method) => COMPONENT_GROUPS_A_URL.test(url) && method === "GET", respond: getGroupsAResponse },
    { test: (url, method) => COMPONENT_GROUPS_B_URL.test(url) && method === "GET", respond: () => groupSetResponse(VECTOR_B_ID, []) },
  ];
}

async function setUpWithLayersAndComponents(extraRoutes: FetchRoute[] = [], getGroupsAResponse?: () => Response) {
  const fetch = stubFetch([...baseRoutes(getGroupsAResponse), ...extraRoutes]);
  vi.stubGlobal("fetch", fetch);

  renderPanel();
  fireEvent.click(screen.getByRole("button", { name: "Generar capas" }));
  await screen.findByRole("listitem", { name: "Capa Color 1" });

  fireEvent.click(screen.getByRole("button", { name: "Calcular componentes" }));
  await screen.findByText(/Color 2: 1 pieza/);
  await screen.findByText("Color 1: 3 piezas");

  return fetch;
}

function selectionCheckbox(pieceLabel: string) {
  return screen.getByRole("checkbox", { name: `Seleccionar ${pieceLabel} para agrupar` });
}

describe("ComponentTree — multi-select confinado a una capa", () => {
  it("seleccionar un componente de otra capa reinicia la selección de la capa anterior", async () => {
    await setUpWithLayersAndComponents();

    fireEvent.click(selectionCheckbox("Pieza 1 de Color 1"));
    expect(selectionCheckbox("Pieza 1 de Color 1")).toBeChecked();

    fireEvent.click(selectionCheckbox("Pieza 1 de Color 2"));

    expect(selectionCheckbox("Pieza 1 de Color 1")).not.toBeChecked();
    expect(selectionCheckbox("Pieza 1 de Color 2")).toBeChecked();
  });

  it("con menos de 2 piezas seleccionadas no aparece el botón Agrupar", async () => {
    await setUpWithLayersAndComponents();

    fireEvent.click(selectionCheckbox("Pieza 1 de Color 1"));

    expect(screen.queryByRole("button", { name: /^Agrupar/ })).not.toBeInTheDocument();
  });
});

describe("ComponentTree — agrupar/desagrupar/renombrar", () => {
  it("agrupar 2+ piezas seleccionadas crea el grupo y lo muestra en el árbol", async () => {
    const fetch = await setUpWithLayersAndComponents([
      {
        test: (url, method) => COMPONENT_GROUPS_A_URL.test(url) && method === "POST",
        respond: (_url, init) => {
          const body = JSON.parse(init?.body as string) as { componentIds: string[] };
          return groupSetResponse(VECTOR_A_ID, [
            { groupId: COMPONENT_GROUP_ID, name: "Grupo 1", componentIds: body.componentIds, isStale: false, missingComponentIds: [] },
          ], 1);
        },
      },
    ]);

    fireEvent.click(selectionCheckbox("Pieza 1 de Color 1"));
    fireEvent.click(selectionCheckbox("Pieza 2 de Color 1"));

    fireEvent.click(screen.getByRole("button", { name: "Agrupar 2 piezas seleccionadas" }));

    expect(await screen.findByRole("button", { name: /Seleccionar como conjunto: Grupo 1/ })).toBeInTheDocument();
    expect(fetch).toHaveBeenCalledWith(
      expect.stringMatching(COMPONENT_GROUPS_A_URL),
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({ componentIds: ["component-1", "component-2"], name: null }),
      }),
    );

    // El botón "Agrupar" desaparece: la selección se limpia tras crear el grupo.
    expect(screen.queryByRole("button", { name: /^Agrupar/ })).not.toBeInTheDocument();
  });

  it("renombrar un grupo llama al endpoint de rename y actualiza el nombre mostrado", async () => {
    let currentGroups = [
      { groupId: COMPONENT_GROUP_ID, name: "Grupo 1", componentIds: ["component-1", "component-2"], isStale: false, missingComponentIds: [] },
    ];

    const fetch = await setUpWithLayersAndComponents(
      [
        {
          test: (url, method) => RENAME_A_URL.test(url) && method === "POST",
          respond: (_url, init) => {
            const body = JSON.parse(init?.body as string) as { name: string };
            currentGroups = currentGroups.map((group) => (group.groupId === COMPONENT_GROUP_ID ? { ...group, name: body.name } : group));
            return groupSetResponse(VECTOR_A_ID, currentGroups, 2);
          },
        },
      ],
      () => groupSetResponse(VECTOR_A_ID, currentGroups, 1),
    );

    const nameInput = await screen.findByRole("textbox", { name: "Nombre del grupo Grupo 1" });
    fireEvent.change(nameInput, { target: { value: "Pieza compuesta" } });
    fireEvent.blur(nameInput);

    expect(await screen.findByRole("textbox", { name: "Nombre del grupo Pieza compuesta" })).toBeInTheDocument();
    expect(fetch).toHaveBeenCalledWith(
      expect.stringMatching(RENAME_A_URL),
      expect.objectContaining({ method: "POST", body: JSON.stringify({ name: "Pieza compuesta" }) }),
    );
  });

  it("desagrupar quita el grupo de la lista", async () => {
    const initialGroups = [
      { groupId: COMPONENT_GROUP_ID, name: "Grupo 1", componentIds: ["component-1", "component-2"], isStale: false, missingComponentIds: [] },
    ];

    const fetch = await setUpWithLayersAndComponents(
      [
        {
          test: (url, method) => UNGROUP_A_URL.test(url) && method === "POST",
          respond: () => groupSetResponse(VECTOR_A_ID, [], 2),
        },
      ],
      () => groupSetResponse(VECTOR_A_ID, initialGroups, 1),
    );

    await screen.findByRole("button", { name: /Seleccionar como conjunto: Grupo 1/ });
    fireEvent.click(screen.getByRole("button", { name: "Desagrupar" }));

    await waitFor(() => expect(screen.queryByRole("button", { name: /Seleccionar como conjunto: Grupo 1/ })).not.toBeInTheDocument());
    expect(fetch).toHaveBeenCalledWith(expect.stringMatching(UNGROUP_A_URL), expect.objectContaining({ method: "POST" }));
  });
});

describe("ComponentTree — seleccionar como conjunto", () => {
  it("clickear un grupo resalta TODOS sus componentes miembro a la vez en el canvas", async () => {
    const initialGroups = [
      { groupId: COMPONENT_GROUP_ID, name: "Grupo 1", componentIds: ["component-1", "component-2"], isStale: false, missingComponentIds: [] },
    ];

    await setUpWithLayersAndComponents([], () => groupSetResponse(VECTOR_A_ID, initialGroups, 1));

    const groupButton = await screen.findByRole("button", { name: /Seleccionar como conjunto: Grupo 1/ });
    fireEvent.click(groupButton);

    expect(groupButton).toHaveAttribute("aria-pressed", "true");

    const canvas = screen.getByRole("group", { name: /Composición combinada/ });
    expect(within(canvas).getByRole("button", { name: "Pieza 1 de Color 1" })).toHaveAttribute("aria-pressed", "true");
    expect(within(canvas).getByRole("button", { name: "Pieza 2 de Color 1" })).toHaveAttribute("aria-pressed", "true");
    expect(within(canvas).getByRole("button", { name: "Pieza 3 de Color 1" })).toHaveAttribute("aria-pressed", "false");

    fireEvent.click(groupButton);
    expect(groupButton).toHaveAttribute("aria-pressed", "false");
  });
});

describe("ComponentTree — grupo stale (referencia un componentId ya no vigente)", () => {
  it("avisa sin romper el árbol cuando un grupo referencia un componentId que ya no existe en el análisis vigente", async () => {
    const staleGroups = [
      {
        groupId: COMPONENT_GROUP_ID,
        name: "Grupo viejo",
        componentIds: ["component-1", "component-9-ya-no-existe"],
        isStale: true,
        missingComponentIds: ["component-9-ya-no-existe"],
      },
    ];

    await setUpWithLayersAndComponents([], () => groupSetResponse(VECTOR_A_ID, staleGroups, 1));

    expect(await screen.findByRole("note")).toHaveTextContent(
      "Uno o más componentes de este grupo ya no existen en el análisis vigente de esta capa.",
    );
    // Sigue siendo operable: renombrar/desagrupar no crashean.
    expect(screen.getByRole("button", { name: "Desagrupar" })).toBeInTheDocument();
  });
});
