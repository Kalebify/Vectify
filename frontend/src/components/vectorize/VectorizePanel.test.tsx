import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { VectorizePanel } from "./VectorizePanel";

const PROJECT_ID = "11111111-1111-1111-1111-111111111111";
const IMAGE_ID = "22222222-2222-2222-2222-222222222222";
const MASK_ID = "33333333-3333-3333-3333-333333333333";
const VECTOR_ID = "44444444-4444-4444-4444-444444444444";

function vectorResponse(overrides: Record<string, unknown> = {}): Response {
  return new Response(
    JSON.stringify({
      projectId: PROJECT_ID,
      imageId: IMAGE_ID,
      vectorId: VECTOR_ID,
      svgUrl: `/api/v1/projects/${PROJECT_ID}/images/${IMAGE_ID}/vectors/${VECTOR_ID}`,
      sourceMaskId: MASK_ID,
      version: 1,
      width: 10,
      height: 10,
      metrics: {
        pathCount: 1,
        approxNodeCount: 4,
        bounds: { minX: 2, minY: 2, maxX: 8, maxY: 8, width: 6, height: 6 },
      },
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
    <VectorizePanel
      projectId={PROJECT_ID}
      imageId={IMAGE_ID}
      fileName="logo.png"
      sourceMaskId={MASK_ID}
      sourceMaskUrl={`/api/v1/projects/${PROJECT_ID}/images/${IMAGE_ID}/masks/${MASK_ID}`}
      sourceWidth={10}
      sourceHeight={10}
    />,
  );
}

beforeEach(() => {
  URL.createObjectURL = () => "blob:mock";
});

describe("VectorizePanel — estado inicial", () => {
  it("no pide nada al montar: el botón dice 'Vectorizar' y no hay resultado todavía", () => {
    const fetch = vi.fn();
    vi.stubGlobal("fetch", fetch);

    renderPanel();

    expect(screen.getByRole("button", { name: "Vectorizar" })).toBeInTheDocument();
    expect(fetch).not.toHaveBeenCalled();
    expect(screen.queryByText("Versión del vector")).not.toBeInTheDocument();
  });
});

describe("VectorizePanel — flujo processing/success", () => {
  it("al pulsar 'Vectorizar' pasa a processing y, al resolver, muestra el SVG y las métricas", async () => {
    const fetch = vi.fn(() => Promise.resolve(vectorResponse()));
    vi.stubGlobal("fetch", fetch);

    renderPanel();

    fireEvent.click(screen.getByRole("button", { name: "Vectorizar" }));

    expect(screen.getByRole("button", { name: "Vectorizando…" })).toBeDisabled();

    expect(await screen.findByText("SVG resultante")).toBeInTheDocument();
    expect(fetch).toHaveBeenCalledWith(
      expect.stringMatching(new RegExp(`/api/v1/projects/${PROJECT_ID}/images/${IMAGE_ID}/vectorize$`)),
      expect.objectContaining({ method: "POST" }),
    );

    await waitFor(() => expect(screen.getByRole("button", { name: "Vectorizar de nuevo" })).toBeEnabled());
    expect(screen.getByText("Paths")).toBeInTheDocument();
    expect(screen.getByText("4")).toBeInTheDocument(); // nodos aproximados
    expect(screen.getByText("6.0 × 6.0")).toBeInTheDocument(); // bounds
  });
});

describe("VectorizePanel — errores controlados", () => {
  it("un error controlado de la Web API se muestra sin romper el panel", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(() => Promise.resolve(errorResponse(422, "empty_mask", "La máscara no tiene ningún píxel de foreground."))),
    );

    renderPanel();
    fireEvent.click(screen.getByRole("button", { name: "Vectorizar" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "La máscara no tiene ningún píxel de foreground.",
    );
    expect(screen.getByRole("button", { name: "Vectorizar" })).toBeEnabled();
  });
});
