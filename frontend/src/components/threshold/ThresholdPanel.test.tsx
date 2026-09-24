import { act, fireEvent, render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ThresholdPanel } from "./ThresholdPanel";

const PROJECT_ID = "11111111-1111-1111-1111-111111111111";
const IMAGE_ID = "22222222-2222-2222-2222-222222222222";
const SOURCE_PREVIEW_ID = "33333333-3333-3333-3333-333333333333";
const MASK_ID = "44444444-4444-4444-4444-444444444444";

function maskResponse(overrides: Record<string, unknown> = {}): Response {
  return new Response(
    JSON.stringify({
      projectId: PROJECT_ID,
      imageId: IMAGE_ID,
      maskId: MASK_ID,
      maskUrl: `/api/v1/projects/${PROJECT_ID}/images/${IMAGE_ID}/masks/${MASK_ID}`,
      sourcePreviewId: SOURCE_PREVIEW_ID,
      version: 1,
      width: 10,
      height: 10,
      effectiveParams: { value: 128, invert: false },
      metrics: {
        foregroundPercent: 40,
        backgroundPercent: 60,
        isNearEmpty: false,
        isNearFull: false,
        warningCode: null,
        warningMessage: null,
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
    <ThresholdPanel
      projectId={PROJECT_ID}
      imageId={IMAGE_ID}
      fileName="logo.png"
      sourcePreviewId={SOURCE_PREVIEW_ID}
      sourcePreviewUrl={`/api/v1/projects/${PROJECT_ID}/images/${IMAGE_ID}/previews/${SOURCE_PREVIEW_ID}`}
      sourceWidth={10}
      sourceHeight={10}
    />,
  );
}

beforeEach(() => {
  URL.createObjectURL = () => "blob:mock";
});

describe("ThresholdPanel — carga inicial", () => {
  it("al montar pide una máscara con los valores por defecto y muestra la comparación y las métricas", async () => {
    const fetch = vi.fn(() => Promise.resolve(maskResponse()));
    vi.stubGlobal("fetch", fetch);

    renderPanel();

    expect(await screen.findByText("Máscara")).toBeInTheDocument();
    expect(fetch).toHaveBeenCalledWith(
      expect.stringMatching(new RegExp(`/api/v1/projects/${PROJECT_ID}/images/${IMAGE_ID}/threshold$`)),
      expect.objectContaining({ method: "POST" }),
    );
    expect(screen.getByText("40.0%")).toBeInTheDocument();
    expect(screen.getByText("1")).toBeInTheDocument(); // versión de la máscara
  });

  it("Restablecer valores está deshabilitado en los valores por defecto y se habilita tras mover el slider", async () => {
    vi.stubGlobal("fetch", vi.fn(() => Promise.resolve(maskResponse())));

    renderPanel();
    await screen.findByText("Máscara");

    expect(screen.getByRole("button", { name: "Restablecer valores" })).toBeDisabled();

    fireEvent.change(screen.getByLabelText("Umbral"), { target: { value: "200" } });

    expect(screen.getByRole("button", { name: "Restablecer valores" })).toBeEnabled();
  });
});

describe("ThresholdPanel — debounce del slider de umbral", () => {
  it("mover el slider actualiza el valor mostrado de inmediato y, recién tras el debounce, pide una máscara nueva", async () => {
    vi.useFakeTimers();
    const fetch = vi.fn(() => Promise.resolve(maskResponse()));
    vi.stubGlobal("fetch", fetch);

    renderPanel();
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0);
    });
    expect(fetch).toHaveBeenCalledTimes(1);

    fireEvent.change(screen.getByLabelText("Umbral"), { target: { value: "200" } });
    expect(screen.getByText("200")).toBeInTheDocument();
    expect(fetch).toHaveBeenCalledTimes(1); // todavía no pasó el debounce

    await act(async () => {
      await vi.advanceTimersByTimeAsync(500);
    });

    expect(fetch).toHaveBeenCalledTimes(2);
  });
});

describe("ThresholdPanel — advertencia de máscara extrema", () => {
  it("muestra la advertencia de la Web API cuando la máscara queda casi vacía", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(() =>
        Promise.resolve(
          maskResponse({
            metrics: {
              foregroundPercent: 0.5,
              backgroundPercent: 99.5,
              isNearEmpty: true,
              isNearFull: false,
              warningCode: "mask_near_empty",
              warningMessage: "La máscara resultante quedó casi vacía.",
            },
          }),
        ),
      ),
    );

    renderPanel();

    expect(await screen.findByRole("alert")).toHaveTextContent("La máscara resultante quedó casi vacía.");
  });
});

describe("ThresholdPanel — errores controlados", () => {
  it("un error controlado de la Web API se muestra sin romper el panel", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(() => Promise.resolve(errorResponse(404, "not_found", "No existe un preview preprocesado con ese ID."))),
    );

    renderPanel();

    expect(await screen.findByRole("alert")).toHaveTextContent("No existe un preview preprocesado con ese ID.");
  });
});
