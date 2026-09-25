import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { SimplifyPanel } from "./SimplifyPanel";

const PROJECT_ID = "11111111-1111-1111-1111-111111111111";
const IMAGE_ID = "22222222-2222-2222-2222-222222222222";
const VECTOR_ID = "33333333-3333-3333-3333-333333333333";
const SIMPLIFICATION_ID = "44444444-4444-4444-4444-444444444444";

const PREVIEW_SVG =
  '<svg xmlns="http://www.w3.org/2000/svg" width="10" height="10"><path d="M2,2 L8,2 L8,8 L2,8 Z"/></svg>';

function metrics(before: number, after: number, reductionPercent: number) {
  return {
    before: { pathCount: 1, approxNodeCount: before, bounds: { minX: 0, minY: 0, maxX: 10, maxY: 10, width: 10, height: 10 } },
    after: { pathCount: 1, approxNodeCount: after, bounds: { minX: 0, minY: 0, maxX: 10, maxY: 10, width: 10, height: 10 } },
    reductionPercent,
  };
}

function previewResponse(overrides: Record<string, unknown> = {}): Response {
  return new Response(
    JSON.stringify({
      projectId: PROJECT_ID,
      imageId: IMAGE_ID,
      sourceVectorId: VECTOR_ID,
      svg: PREVIEW_SVG,
      width: 10,
      height: 10,
      metrics: metrics(12, 4, 66.7),
      preset: "medium",
      tolerance: 0.004,
      ...overrides,
    }),
    { status: 200, headers: { "Content-Type": "application/json" } },
  );
}

function applyResponse(overrides: Record<string, unknown> = {}): Response {
  return new Response(
    JSON.stringify({
      projectId: PROJECT_ID,
      imageId: IMAGE_ID,
      simplificationId: SIMPLIFICATION_ID,
      svgUrl: `/api/v1/projects/${PROJECT_ID}/images/${IMAGE_ID}/simplifications/${SIMPLIFICATION_ID}`,
      sourceVectorId: VECTOR_ID,
      version: 1,
      width: 10,
      height: 10,
      metrics: metrics(12, 4, 66.7),
      preset: "medium",
      tolerance: 0.004,
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
    <SimplifyPanel
      projectId={PROJECT_ID}
      imageId={IMAGE_ID}
      fileName="logo.png"
      sourceVectorId={VECTOR_ID}
      currentVectorUrl={`/api/v1/projects/${PROJECT_ID}/images/${IMAGE_ID}/vectors/${VECTOR_ID}`}
      currentVectorWidth={10}
      currentVectorHeight={10}
    />,
  );
}

describe("SimplifyPanel — estado inicial", () => {
  it("no pide nada al montar: preset Medio por defecto y sin resultado todavía", () => {
    const fetch = vi.fn();
    vi.stubGlobal("fetch", fetch);

    renderPanel();

    expect(screen.getByRole("radio", { name: "Medio" })).toBeChecked();
    expect(screen.getByRole("button", { name: "Vista previa" })).toBeInTheDocument();
    expect(fetch).not.toHaveBeenCalled();
    expect(screen.queryByText("Nodos antes")).not.toBeInTheDocument();
  });
});

describe("SimplifyPanel — flujo de preview", () => {
  it("al pulsar 'Vista previa' pide el preview y muestra nodeCount antes/después y % de reducción, sin persistir", async () => {
    const fetch = vi.fn((_input: RequestInfo | URL, _init?: RequestInit) => Promise.resolve(previewResponse()));
    vi.stubGlobal("fetch", fetch);

    renderPanel();
    fireEvent.click(screen.getByRole("button", { name: "Vista previa" }));

    expect(await screen.findByText("Nodos antes")).toBeInTheDocument();
    expect(screen.getByText("12")).toBeInTheDocument();
    expect(screen.getByText("4")).toBeInTheDocument();
    expect(screen.getByText("66.7%")).toBeInTheDocument();
    expect(screen.getByText("Vector actual")).toBeInTheDocument();
    expect(screen.getByText("Preview simplificado")).toBeInTheDocument();

    expect(fetch).toHaveBeenCalledWith(
      expect.stringMatching(new RegExp(`/api/v1/projects/${PROJECT_ID}/images/${IMAGE_ID}/simplify/preview$`)),
      expect.objectContaining({ method: "POST" }),
    );
    const init = fetch.mock.calls[0][1] as RequestInit;
    expect(JSON.parse(init.body as string)).toEqual({ vectorId: VECTOR_ID, preset: "medium", tolerance: null });

    // Reversible: no debería haber pedido crear/consultar ninguna simplificación persistida.
    expect(fetch).toHaveBeenCalledTimes(1);
  });

  it("muestra Aplicar y Cancelar solo después de un preview exitoso", async () => {
    vi.stubGlobal("fetch", vi.fn(() => Promise.resolve(previewResponse())));

    renderPanel();
    expect(screen.queryByRole("button", { name: "Aplicar" })).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Vista previa" }));

    expect(await screen.findByRole("button", { name: "Aplicar" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Cancelar" })).toBeInTheDocument();
  });
});

describe("SimplifyPanel — cancelar", () => {
  it("Cancelar descarta el preview sin llamar de nuevo a la Web API", async () => {
    const fetch = vi.fn(() => Promise.resolve(previewResponse()));
    vi.stubGlobal("fetch", fetch);

    renderPanel();
    fireEvent.click(screen.getByRole("button", { name: "Vista previa" }));
    await screen.findByRole("button", { name: "Aplicar" });

    fireEvent.click(screen.getByRole("button", { name: "Cancelar" }));

    expect(screen.queryByRole("button", { name: "Aplicar" })).not.toBeInTheDocument();
    expect(screen.queryByText("Nodos antes")).not.toBeInTheDocument();
    expect(fetch).toHaveBeenCalledTimes(1); // solo el preview inicial, cancelar no llama a la red
  });
});

describe("SimplifyPanel — aplicar", () => {
  it("Aplicar persiste la simplificación y muestra la versión creada", async () => {
    const fetch = vi.fn((input: RequestInfo | URL) => {
      const url = input.toString();
      if (url.includes("/simplify/apply")) {
        return Promise.resolve(applyResponse());
      }
      return Promise.resolve(previewResponse());
    });
    vi.stubGlobal("fetch", fetch);

    renderPanel();
    fireEvent.click(screen.getByRole("button", { name: "Vista previa" }));
    await screen.findByRole("button", { name: "Aplicar" });

    fireEvent.click(screen.getByRole("button", { name: "Aplicar" }));

    expect(await screen.findByText(/Simplificación aplicada como versión 1/)).toBeInTheDocument();
    await waitFor(() => expect(screen.queryByRole("button", { name: "Aplicar" })).not.toBeInTheDocument());
    expect(fetch).toHaveBeenCalledWith(
      expect.stringMatching(new RegExp(`/api/v1/projects/${PROJECT_ID}/images/${IMAGE_ID}/simplify/apply$`)),
      expect.objectContaining({ method: "POST" }),
    );
  });
});

describe("SimplifyPanel — errores controlados", () => {
  it("un error controlado de la Web API se muestra sin romper el panel", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(() => Promise.resolve(errorResponse(404, "not_found", "No existe un SVG vectorizado con ese ID."))),
    );

    renderPanel();
    fireEvent.click(screen.getByRole("button", { name: "Vista previa" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("No existe un SVG vectorizado con ese ID.");
    expect(screen.getByRole("button", { name: "Vista previa" })).toBeEnabled();
  });
});

describe("SimplifyPanel — selección de preset", () => {
  it("cambiar de preset envía el nuevo valor en la próxima vista previa", async () => {
    const fetch = vi.fn((_input: RequestInfo | URL, _init?: RequestInit) =>
      Promise.resolve(previewResponse({ preset: "high", tolerance: 0.012 })),
    );
    vi.stubGlobal("fetch", fetch);

    renderPanel();
    fireEvent.click(screen.getByRole("radio", { name: "Alto" }));
    fireEvent.click(screen.getByRole("button", { name: "Vista previa" }));

    await screen.findByText("Nodos antes");
    const init = fetch.mock.calls[0][1] as RequestInit;
    expect(JSON.parse(init.body as string)).toEqual({ vectorId: VECTOR_ID, preset: "high", tolerance: null });
  });
});
