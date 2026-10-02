import { act, renderHook, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { useWorkspaceSave } from "./useWorkspaceSave";

const CLASSIC_PROJECT_ID = "11111111-1111-1111-1111-111111111111";
const IMAGE_ID = "22222222-2222-2222-2222-222222222222";
const PALETTE_ID = "33333333-3333-3333-3333-333333333333";
const SAVED_PROJECT_ID = "44444444-4444-4444-4444-444444444444";

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

function baseParams(overrides: Partial<Parameters<typeof useWorkspaceSave>[0]> = {}) {
  return {
    initialSavedProjectId: null,
    projectName: "Mi diseño",
    classicProjectId: CLASSIC_PROJECT_ID,
    imageId: IMAGE_ID,
    paletteId: PALETTE_ID,
    paletteVersion: 1,
    dimensionId: null,
    ...overrides,
  };
}

describe("useWorkspaceSave — máquina de estados idle/dirty/saving/saved/error", () => {
  it("arranca en 'idle' y markDirty lo pasa a 'dirty'", () => {
    const { result } = renderHook(() => useWorkspaceSave(baseParams()));

    expect(result.current.state).toBe("idle");

    act(() => result.current.markDirty());

    expect(result.current.state).toBe("dirty");
  });

  it("save() pasa por 'saving' y SOLO llega a 'saved' tras la confirmación real del backend (nunca optimista)", async () => {
    let resolveFetch!: (value: Response) => void;
    const fetch = vi.fn(
      () =>
        new Promise<Response>((resolve) => {
          resolveFetch = resolve;
        }),
    );
    vi.stubGlobal("fetch", fetch);

    const { result } = renderHook(() => useWorkspaceSave(baseParams()));

    act(() => result.current.markDirty());
    expect(result.current.state).toBe("dirty");

    act(() => result.current.save());
    expect(result.current.state).toBe("saving");
    // Todavía no hay confirmación del backend -- JAMÁS debe mostrarse "saved" acá.
    expect(result.current.state).not.toBe("saved");

    resolveFetch(jsonResponse({ projectId: SAVED_PROJECT_ID, versionNumber: 1, savedAt: "2026-01-01T00:00:00Z" }, 201));

    await waitFor(() => expect(result.current.state).toBe("saved"));
    expect(result.current.savedProjectId).toBe(SAVED_PROJECT_ID);
  });

  it("manda projectId=null y name en el primer Save (sin savedProjectId todavía)", async () => {
    const fetch = vi.fn((_input: string, _init?: RequestInit) =>
      Promise.resolve(jsonResponse({ projectId: SAVED_PROJECT_ID, versionNumber: 1, savedAt: "2026-01-01T00:00:00Z" }, 201)),
    );
    vi.stubGlobal("fetch", fetch);

    const { result } = renderHook(() => useWorkspaceSave(baseParams({ projectName: "Proyecto nuevo" })));

    act(() => result.current.save());
    await waitFor(() => expect(result.current.state).toBe("saved"));

    const [, init] = fetch.mock.calls[0];
    const body = JSON.parse(init!.body as string);
    expect(body.projectId).toBeNull();
    expect(body.name).toBe("Proyecto nuevo");
    expect(body.classicProjectId).toBe(CLASSIC_PROJECT_ID);
    expect(body.paletteId).toBe(PALETTE_ID);
  });

  it("en Saves subsiguientes manda el projectId ya conocido y name null (no renombra sin que el usuario lo pida)", async () => {
    const fetch = vi.fn((_input: string, _init?: RequestInit) =>
      Promise.resolve(jsonResponse({ projectId: SAVED_PROJECT_ID, versionNumber: 2, savedAt: "2026-01-01T00:00:00Z" }, 200)),
    );
    vi.stubGlobal("fetch", fetch);

    const { result } = renderHook(() => useWorkspaceSave(baseParams({ initialSavedProjectId: SAVED_PROJECT_ID })));

    act(() => result.current.save());
    await waitFor(() => expect(result.current.state).toBe("saved"));

    const [, init] = fetch.mock.calls[0];
    const body = JSON.parse(init!.body as string);
    expect(body.projectId).toBe(SAVED_PROJECT_ID);
    expect(body.name).toBeNull();
  });

  it("falla (red/409/422/500) -> estado 'error' con mensaje visible, reintentable sin perder el intento", async () => {
    const fetch = vi.fn(() =>
      Promise.resolve(jsonResponse({ code: "palette_not_confirmed", message: "La paleta debe estar confirmada." }, 422)),
    );
    vi.stubGlobal("fetch", fetch);

    const { result } = renderHook(() => useWorkspaceSave(baseParams()));

    act(() => result.current.save());
    await waitFor(() => expect(result.current.state).toBe("error"));
    expect(result.current.errorMessage).toBe("La paleta debe estar confirmada.");

    // Reintentable: un segundo save() vuelve a disparar la llamada sin que el hook quede trabado.
    const secondFetch = vi.fn(() =>
      Promise.resolve(jsonResponse({ projectId: SAVED_PROJECT_ID, versionNumber: 1, savedAt: "2026-01-01T00:00:00Z" }, 201)),
    );
    vi.stubGlobal("fetch", secondFetch);

    act(() => result.current.save());
    await waitFor(() => expect(result.current.state).toBe("saved"));
  });

  it("no dispara ningún fetch si todavía no hay paletteId/paletteVersion resueltos (documento no cargado)", () => {
    const fetch = vi.fn();
    vi.stubGlobal("fetch", fetch);

    const { result } = renderHook(() => useWorkspaceSave(baseParams({ paletteId: null, paletteVersion: null })));

    act(() => result.current.save());

    expect(fetch).not.toHaveBeenCalled();
    expect(result.current.state).toBe("idle");
  });
});
