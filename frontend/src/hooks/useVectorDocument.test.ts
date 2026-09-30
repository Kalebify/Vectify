import { act, renderHook, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { useVectorDocument } from "./useVectorDocument";

const PROJECT_ID = "11111111-1111-1111-1111-111111111111";
const IMAGE_ID = "22222222-2222-2222-2222-222222222222";
const PALETTE_ID = "33333333-3333-3333-3333-333333333333";
const GROUP_A_ID = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
const GROUP_B_ID = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
const VECTOR_A_ID = "cccccccc-cccc-cccc-cccc-cccccccccccc";
const VECTOR_B_ID = "dddddddd-dddd-dddd-dddd-dddddddddddd";
const LAYER_SET_ID = "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee";

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

function paletteResponse(overrides: Record<string, unknown> = {}) {
  return {
    projectId: PROJECT_ID,
    imageId: IMAGE_ID,
    paletteId: PALETTE_ID,
    version: 2,
    tolerance: 24,
    maxColors: null,
    tinyAreaRatio: 0.01,
    sourceWidthPx: 320,
    sourceHeightPx: 240,
    transparentPercent: 0,
    groups: [
      { groupId: GROUP_A_ID, name: "Rojo", colorHex: "#ff0000", rgb: { r: 255, g: 0, b: 0 }, pixelCount: 100, areaPercent: 60, hasPartialAlpha: false, isExcluded: false, maskUrl: "/mask/a", isMerged: false },
      { groupId: GROUP_B_ID, name: "Azul", colorHex: "#0000ff", rgb: { r: 0, g: 0, b: 255 }, pixelCount: 60, areaPercent: 40, hasPartialAlpha: false, isExcluded: false, maskUrl: "/mask/b", isMerged: false },
    ],
    previewUrl: "/preview",
    isConfirmed: true,
    cached: false,
    ...overrides,
  };
}

function layerSetResponse(overrides: Record<string, unknown> = {}) {
  return {
    projectId: PROJECT_ID,
    imageId: IMAGE_ID,
    layerSetId: LAYER_SET_ID,
    version: 1,
    paletteId: PALETTE_ID,
    paletteVersion: 2,
    sourceWidthPx: 320,
    sourceHeightPx: 240,
    layers: [
      { groupId: GROUP_A_ID, name: "Rojo", colorHex: "#ff0000", areaPercent: 60, hasPartialAlpha: false, vectorId: VECTOR_A_ID, svgUrl: `/vectors/${VECTOR_A_ID}` },
      { groupId: GROUP_B_ID, name: "Azul", colorHex: "#0000ff", areaPercent: 40, hasPartialAlpha: false, vectorId: VECTOR_B_ID, svgUrl: `/vectors/${VECTOR_B_ID}` },
    ],
    cached: true,
    ...overrides,
  };
}

function consolidatedResponse(overrides: Record<string, unknown> = {}) {
  return {
    projectId: PROJECT_ID,
    imageId: IMAGE_ID,
    layerSetId: LAYER_SET_ID,
    version: 1,
    paletteId: PALETTE_ID,
    paletteVersion: 2,
    sourceWidthPx: 320,
    sourceHeightPx: 240,
    layers: [
      { id: GROUP_A_ID, name: "Rojo", colorHex: "#ff0000", fill: "#ff0000", vectorId: VECTOR_A_ID, svgUrl: `/vectors/${VECTOR_A_ID}`, pathCount: 3, componentCount: 1, manufacturingOperation: "cut", visible: true, locked: false, order: 0, rasterValidation: { ownMismatchRatio: 0, ownMismatchTolerance: 0.02, ownMismatchWithinTolerance: true, contaminationRatio: 0, contaminationTolerance: 0.02, contaminationWithinTolerance: true, warnings: [] } },
      { id: GROUP_B_ID, name: "Azul", colorHex: "#0000ff", fill: "#0000ff", vectorId: VECTOR_B_ID, svgUrl: `/vectors/${VECTOR_B_ID}`, pathCount: 2, componentCount: null, manufacturingOperation: "unassigned", visible: true, locked: false, order: 1, rasterValidation: { ownMismatchRatio: 0, ownMismatchTolerance: 0.02, ownMismatchWithinTolerance: true, contaminationRatio: 0, contaminationTolerance: 0.02, contaminationWithinTolerance: true, warnings: [] } },
    ],
    ...overrides,
  };
}

function stubFetchSequence(handlers: Array<(url: string) => Response>) {
  const fetch = vi.fn((input: string | URL) => {
    const url = String(input);
    for (const handler of handlers) {
      const result = handler(url);
      if (result) return Promise.resolve(result);
    }
    return Promise.reject(new Error(`Unhandled fetch: ${url}`));
  });
  vi.stubGlobal("fetch", fetch);
  return fetch;
}

function stubHappyPath() {
  return stubFetchSequence([
    (url) => (url.includes("/layers/consolidated") ? jsonResponse(consolidatedResponse()) : undefined!),
    (url) => (/\/layers$/.test(url) ? jsonResponse(layerSetResponse()) : undefined!),
    (url) => (url.endsWith(`/color-palette/${PALETTE_ID}`) ? jsonResponse(paletteResponse()) : undefined!),
  ]);
}

describe("useVectorDocument — sin paletteId", () => {
  it("queda en estado 'empty' (no_palette_selected) sin pedir nada", () => {
    const fetch = vi.fn();
    vi.stubGlobal("fetch", fetch);

    const { result } = renderHook(() => useVectorDocument(PROJECT_ID, IMAGE_ID, null));

    expect(result.current.status).toBe("empty");
    expect(result.current.emptyReason).toBe("no_palette_selected");
    expect(fetch).not.toHaveBeenCalled();
  });
});

describe("useVectorDocument — proyecto multicolor (caso principal)", () => {
  it("agrega paleta confirmada + capas + consolidado en un único VectorDocument", async () => {
    stubHappyPath();

    const { result } = renderHook(() => useVectorDocument(PROJECT_ID, IMAGE_ID, PALETTE_ID));

    await waitFor(() => expect(result.current.status).toBe("ready"));

    expect(result.current.document?.layers).toHaveLength(2);
    expect(result.current.document?.layers[0]).toMatchObject({
      groupId: GROUP_A_ID,
      name: "Rojo",
      colorHex: "#ff0000",
      pathCount: 3,
      componentCount: 1,
      manufacturingOperation: "cut",
      areaPercent: 60,
    });
    expect(result.current.visibility).toEqual({ [GROUP_A_ID]: true, [GROUP_B_ID]: true });
  });
});

describe("useVectorDocument — proyecto sin layers generadas", () => {
  it("queda en 'empty' (layers_not_generated) sin inventar capas", async () => {
    stubFetchSequence([
      (url) => (/\/layers$/.test(url) ? jsonResponse({ code: "not_found", message: "No existe un conjunto de capas generado para esa paleta." }, 404) : undefined!),
      (url) => (url.endsWith(`/color-palette/${PALETTE_ID}`) ? jsonResponse(paletteResponse()) : undefined!),
    ]);

    const { result } = renderHook(() => useVectorDocument(PROJECT_ID, IMAGE_ID, PALETTE_ID));

    await waitFor(() => expect(result.current.status).toBe("empty"));
    expect(result.current.emptyReason).toBe("layers_not_generated");
    expect(result.current.document).toBeNull();
  });
});

describe("useVectorDocument — paleta no confirmada", () => {
  it("queda en 'empty' (palette_not_confirmed), nunca pide el conjunto de capas", async () => {
    const fetch = stubFetchSequence([
      (url) => (url.endsWith(`/color-palette/${PALETTE_ID}`) ? jsonResponse(paletteResponse({ isConfirmed: false })) : undefined!),
    ]);

    const { result } = renderHook(() => useVectorDocument(PROJECT_ID, IMAGE_ID, PALETTE_ID));

    await waitFor(() => expect(result.current.status).toBe("empty"));
    expect(result.current.emptyReason).toBe("palette_not_confirmed");
    expect(fetch.mock.calls.some((call) => String(call[0]).match(/\/layers$/))).toBe(false);
  });
});

describe("useVectorDocument — error de carga", () => {
  it("paleta inexistente (404) -> 'empty' (palette_not_found), no 'error'", async () => {
    stubFetchSequence([
      (url) =>
        url.endsWith(`/color-palette/${PALETTE_ID}`)
          ? jsonResponse({ code: "not_found", message: "No existe." }, 404)
          : undefined!,
    ]);

    const { result } = renderHook(() => useVectorDocument(PROJECT_ID, IMAGE_ID, PALETTE_ID));

    await waitFor(() => expect(result.current.status).toBe("empty"));
    expect(result.current.emptyReason).toBe("palette_not_found");
  });

  it("falla de red real -> estado 'error' con mensaje honesto", async () => {
    vi.stubGlobal("fetch", vi.fn(() => Promise.reject(new Error("network down"))));

    const { result } = renderHook(() => useVectorDocument(PROJECT_ID, IMAGE_ID, PALETTE_ID));

    await waitFor(() => expect(result.current.status).toBe("error"));
    expect(result.current.errorMessage).toBeTruthy();
  });
});

describe("useVectorDocument — visibilidad/selección compartidas", () => {
  it("toggleVisibility/isolate/showAll operan sobre el mismo record que consumen todos los paneles", async () => {
    stubHappyPath();
    const { result } = renderHook(() => useVectorDocument(PROJECT_ID, IMAGE_ID, PALETTE_ID));
    await waitFor(() => expect(result.current.status).toBe("ready"));

    act(() => result.current.toggleVisibility(GROUP_A_ID));
    expect(result.current.visibility[GROUP_A_ID]).toBe(false);

    act(() => result.current.isolate(GROUP_B_ID));
    expect(result.current.visibility).toEqual({ [GROUP_A_ID]: false, [GROUP_B_ID]: true });

    act(() => result.current.showAll());
    expect(result.current.visibility).toEqual({ [GROUP_A_ID]: true, [GROUP_B_ID]: true });
  });

  it("selectGroup sincroniza la selección compartida", async () => {
    stubHappyPath();
    const { result } = renderHook(() => useVectorDocument(PROJECT_ID, IMAGE_ID, PALETTE_ID));
    await waitFor(() => expect(result.current.status).toBe("ready"));

    expect(result.current.selectedGroupId).toBeNull();
    act(() => result.current.selectGroup(GROUP_A_ID));
    expect(result.current.selectedGroupId).toBe(GROUP_A_ID);
  });
});
