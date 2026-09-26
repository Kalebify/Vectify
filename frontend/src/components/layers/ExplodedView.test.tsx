import { fireEvent, render, screen, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { LayersPanel } from "./LayersPanel";

const PROJECT_ID = "11111111-1111-1111-1111-111111111111";
const IMAGE_ID = "22222222-2222-2222-2222-222222222222";
const PALETTE_ID = "33333333-3333-3333-3333-333333333333";

const LAYERS_URL = new RegExp(`/color-palette/${PALETTE_ID}/layers$`);

function groupId(index: number): string {
  return `group-${index}`;
}

function vectorId(index: number): string {
  return `vector-${index}`;
}

function componentsUrl(index: number): RegExp {
  return new RegExp(`/vectors/${vectorId(index)}/components$`);
}

/** Genera `count` capas (una por color), cada una con su propio groupId/vectorId deterministas. */
function makeLayers(count: number) {
  return Array.from({ length: count }, (_, index) => ({
    groupId: groupId(index),
    name: `Color ${index + 1}`,
    colorHex: `#${(index + 1).toString(16).padStart(6, "0")}`,
    areaPercent: Math.round(100 / count),
    hasPartialAlpha: false,
    vectorId: vectorId(index),
    svgUrl: `/api/v1/projects/${PROJECT_ID}/images/${IMAGE_ID}/vectors/${vectorId(index)}`,
  }));
}

function layerSetResponse(count: number, sourceSizePx = 100): Response {
  return new Response(
    JSON.stringify({
      projectId: PROJECT_ID,
      imageId: IMAGE_ID,
      layerSetId: "layer-set-1",
      version: 1,
      paletteId: PALETTE_ID,
      paletteVersion: 1,
      sourceWidthPx: sourceSizePx,
      sourceHeightPx: sourceSizePx,
      layers: makeLayers(count),
      cached: false,
    }),
    { status: 201, headers: { "Content-Type": "application/json" } },
  );
}

function makeComponents(count: number, boxSize = 10) {
  return Array.from({ length: count }, (_, index) => ({
    id: `component-${index}`,
    members: [
      {
        pathIndex: index,
        subpathIndex: 0,
        role: "solid",
        bounds: { minX: index * boxSize, minY: 0, maxX: index * boxSize + boxSize, maxY: boxSize },
        area: boxSize * boxSize,
      },
    ],
    bounds: { minX: index * boxSize, minY: 0, maxX: index * boxSize + boxSize, maxY: boxSize },
    area: boxSize * boxSize,
    isTiny: false,
  }));
}

function componentsResponse(vectorIdValue: string, componentCount: number): Response {
  return new Response(
    JSON.stringify({
      projectId: PROJECT_ID,
      imageId: IMAGE_ID,
      componentSetId: `component-set-${vectorIdValue}`,
      version: 1,
      vectorId: vectorIdValue,
      components: makeComponents(componentCount),
      skippedPathCount: 0,
      cached: false,
    }),
    { status: 201, headers: { "Content-Type": "application/json" } },
  );
}

function renderPanel() {
  return render(<LayersPanel projectId={PROJECT_ID} imageId={IMAGE_ID} paletteId={PALETTE_ID} />);
}

/** fetch fake: capas + componentes por vectorId, con conteo de componentes configurable por índice de capa. */
function stubFetch(layerCount: number, componentsCountByLayerIndex: Record<number, number> = {}) {
  const fetch = vi.fn((input: RequestInfo | URL) => {
    const url = typeof input === "string" ? input : input.toString();
    if (LAYERS_URL.test(url)) {
      return Promise.resolve(layerSetResponse(layerCount));
    }
    for (let index = 0; index < layerCount; index += 1) {
      if (componentsUrl(index).test(url)) {
        return Promise.resolve(componentsResponse(vectorId(index), componentsCountByLayerIndex[index] ?? 1));
      }
    }
    throw new Error(`URL no simulada: ${url}`);
  });
  vi.stubGlobal("fetch", fetch);
  return fetch;
}

async function generateLayers() {
  renderPanel();
  fireEvent.click(screen.getByRole("button", { name: "Generar capas" }));
  await screen.findByRole("listitem", { name: "Capa Color 1" });
}

function switchToExploded() {
  fireEvent.click(screen.getByRole("radio", { name: "Explotada" }));
}

function switchToAssembled() {
  fireEvent.click(screen.getByRole("radio", { name: "Ensamblada" }));
}

function setSeparation(value: number) {
  fireEvent.change(screen.getByLabelText("Separación entre capas"), { target: { value: String(value) } });
}

function layerGroupElements(container: HTMLElement): HTMLElement[] {
  return Array.from(container.querySelectorAll(".layer-canvas__layer-group"));
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("Vista explotada — toggle explícito", () => {
  it("arranca en vista ensamblada y permite alternar a explotada y volver", async () => {
    stubFetch(2);
    await generateLayers();

    expect(screen.getByRole("radio", { name: "Ensamblada" })).toBeChecked();
    expect(screen.getByRole("group", { name: /Composición combinada de 2 de 2 capas visibles/ })).toBeInTheDocument();

    switchToExploded();
    expect(screen.getByRole("radio", { name: "Explotada" })).toBeChecked();
    expect(screen.getByRole("group", { name: /Vista explotada de 2 de 2 capas visibles/ })).toBeInTheDocument();

    switchToAssembled();
    expect(screen.getByRole("group", { name: /Composición combinada de 2 de 2 capas visibles/ })).toBeInTheDocument();
  });
});

describe("Vista explotada — desplazamiento puramente visual", () => {
  it.each([2, 5, 10])(
    "con %i capas, cada capa se desplaza proporcionalmente a su índice y ninguna geometría cambia",
    async (layerCount) => {
      stubFetch(layerCount);
      await generateLayers();

      switchToExploded();
      setSeparation(30);

      const canvas = screen.getByRole("group", { name: /Vista explotada/ });
      const groups = layerGroupElements(canvas);
      expect(groups).toHaveLength(layerCount);

      groups.forEach((group, index) => {
        if (index === 0) {
          expect(group.style.transform).toBe("");
        } else {
          expect(group.style.transform).toBe(`translate(${index * 30}%, ${index * 30}%)`);
        }
      });

      // Volver a ensamblada recupera exactamente la posición original: sin
      // transform alguno, no una aproximación a 0.
      switchToAssembled();
      const assembledCanvas = screen.getByRole("group", { name: /Composición combinada/ });
      layerGroupElements(assembledCanvas).forEach((group) => {
        expect(group.style.transform).toBe("");
      });
    },
  );

  it("la separación no tiene límite superior estricto", async () => {
    stubFetch(3);
    await generateLayers();

    switchToExploded();
    setSeparation(5000);

    const canvas = screen.getByRole("group", { name: /Vista explotada/ });
    const groups = layerGroupElements(canvas);
    expect(groups[2].style.transform).toBe("translate(10000%, 10000%)");
  });
});

describe("Vista explotada — aislar color reutilizando el toggle de M2-S02", () => {
  it("ocultar una capa la saca de ambas vistas, sin duplicar el estado de visibilidad", async () => {
    stubFetch(3);
    await generateLayers();

    switchToExploded();
    fireEvent.click(screen.getByRole("checkbox", { name: "Ocultar la capa Color 1" }));

    expect(
      screen.getByRole("group", { name: /Vista explotada de 2 de 3 capas visibles/ }),
    ).toBeInTheDocument();

    switchToAssembled();
    expect(
      screen.getByRole("group", { name: /Composición combinada de 2 de 3 capas visibles/ }),
    ).toBeInTheDocument();
  });
});

describe("Vista explotada — leyenda y contador de piezas", () => {
  it("la leyenda solo aparece en la vista explotada, con nombre y color por capa", async () => {
    stubFetch(2);
    await generateLayers();

    expect(screen.queryByRole("list", { name: "Leyenda de colores y piezas de la vista explotada" })).not.toBeInTheDocument();

    switchToExploded();
    const legend = screen.getByRole("list", { name: "Leyenda de colores y piezas de la vista explotada" });
    expect(within(legend).getByText("Color 1")).toBeInTheDocument();
    expect(within(legend).getByText("Color 2")).toBeInTheDocument();
  });

  it("el contador de piezas usa el dato YA CALCULADO de M2-S03, sin recalcular al cambiar de vista", async () => {
    const fetch = stubFetch(2, { 0: 3, 1: 1 });
    await generateLayers();

    fireEvent.click(screen.getByRole("button", { name: "Calcular componentes" }));
    await screen.findByText("Color 1: 3 piezas");

    const callsAfterCompute = fetch.mock.calls.length;

    switchToExploded();
    const legend = screen.getByRole("list", { name: "Leyenda de colores y piezas de la vista explotada" });
    expect(within(legend).getByText("3 piezas")).toBeInTheDocument();
    expect(within(legend).getByText("1 pieza")).toBeInTheDocument();

    // Alternar de vista no dispara ningún fetch nuevo (no recalcula nada).
    expect(fetch.mock.calls.length).toBe(callsAfterCompute);
  });

  it("una capa con muchos componentes reporta el conteo correcto en la leyenda y todas sus piezas en el canvas", async () => {
    stubFetch(1, { 0: 8 });
    await generateLayers();

    fireEvent.click(screen.getByRole("button", { name: "Calcular componentes" }));
    await screen.findByText("Color 1: 8 piezas");

    switchToExploded();
    const legend = screen.getByRole("list", { name: "Leyenda de colores y piezas de la vista explotada" });
    expect(within(legend).getByText("8 piezas")).toBeInTheDocument();

    const canvas = screen.getByRole("group", { name: /Vista explotada/ });
    const pieceButtons = within(canvas).getAllByRole("button", { name: /Pieza \d de Color 1/ });
    expect(pieceButtons).toHaveLength(8);
  });
});

describe("Vista explotada — selección consistente entre vistas", () => {
  it("seleccionar una pieza en ensamblada y cambiar a explotada mantiene la selección", async () => {
    stubFetch(2);
    await generateLayers();

    fireEvent.click(screen.getByRole("button", { name: "Calcular componentes" }));
    await screen.findByText("Color 1: 1 pieza");

    const listBefore = screen.getByRole("list", { name: "Árbol de capas y sus componentes físicos" });
    fireEvent.click(within(listBefore).getByRole("button", { name: "Pieza 1 de Color 1" }));

    switchToExploded();

    const canvas = screen.getByRole("group", { name: /Vista explotada/ });
    expect(within(canvas).getByRole("button", { name: "Pieza 1 de Color 1" })).toHaveAttribute("aria-pressed", "true");

    switchToAssembled();
    const assembledCanvas = screen.getByRole("group", { name: /Composición combinada/ });
    expect(within(assembledCanvas).getByRole("button", { name: "Pieza 1 de Color 1" })).toHaveAttribute(
      "aria-pressed",
      "true",
    );
  });

  it("seleccionar una pieza en explotada y volver a ensamblada mantiene la selección", async () => {
    stubFetch(2);
    await generateLayers();

    fireEvent.click(screen.getByRole("button", { name: "Calcular componentes" }));
    await screen.findByText("Color 1: 1 pieza");

    switchToExploded();
    const canvas = screen.getByRole("group", { name: /Vista explotada/ });
    fireEvent.click(within(canvas).getByRole("button", { name: "Pieza 1 de Color 2" }));

    switchToAssembled();
    const assembledCanvas = screen.getByRole("group", { name: /Composición combinada/ });
    expect(within(assembledCanvas).getByRole("button", { name: "Pieza 1 de Color 2" })).toHaveAttribute(
      "aria-pressed",
      "true",
    );
  });
});

describe("Vista explotada — responsive", () => {
  it("no se rompe en un viewport angosto: los controles y el canvas siguen accesibles", async () => {
    stubFetch(3);
    await generateLayers();

    Object.defineProperty(window, "innerWidth", { writable: true, configurable: true, value: 320 });
    window.dispatchEvent(new Event("resize"));

    switchToExploded();

    expect(screen.getByRole("radio", { name: "Explotada" })).toBeChecked();
    expect(screen.getByRole("group", { name: /Vista explotada/ })).toBeInTheDocument();
    expect(screen.getByRole("list", { name: "Leyenda de colores y piezas de la vista explotada" })).toBeInTheDocument();
    expect(screen.getByLabelText("Separación entre capas")).toBeInTheDocument();
  });
});
