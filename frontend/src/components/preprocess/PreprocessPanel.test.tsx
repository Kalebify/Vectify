import { act, fireEvent, render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { PreprocessPanel } from "./PreprocessPanel";

const PROJECT_ID = "11111111-1111-1111-1111-111111111111";
const IMAGE_ID = "22222222-2222-2222-2222-222222222222";
const PREVIEW_ID = "33333333-3333-3333-3333-333333333333";

function previewResponse(overrides: Record<string, unknown> = {}): Response {
  return new Response(
    JSON.stringify({
      projectId: PROJECT_ID,
      imageId: IMAGE_ID,
      previewId: PREVIEW_ID,
      previewUrl: `/api/v1/projects/${PROJECT_ID}/images/${IMAGE_ID}/previews/${PREVIEW_ID}`,
      version: 1,
      width: 10,
      height: 10,
      originalWidth: 10,
      originalHeight: 10,
      effectiveParams: { grayscale: false, contrast: 1, brightness: 0, denoise: 0 },
      metrics: { meanBrightness: 128, stdDev: 12, minValue: 0, maxValue: 255 },
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
    <PreprocessPanel
      projectId={PROJECT_ID}
      imageId={IMAGE_ID}
      fileName="logo.png"
      originalWidth={10}
      originalHeight={10}
    />,
  );
}

beforeEach(() => {
  // jsdom no implementa createObjectURL; no lo usamos acá directamente, pero
  // algún componente hijo podría montarse en el mismo árbol de test.
  URL.createObjectURL = () => "blob:mock";
});

describe("PreprocessPanel — carga inicial", () => {
  it("al montar pide un preview con los valores por defecto y muestra la comparación y las métricas", async () => {
    const fetch = vi.fn(() => Promise.resolve(previewResponse()));
    vi.stubGlobal("fetch", fetch);

    renderPanel();

    expect(await screen.findByText("Preprocesada")).toBeInTheDocument();
    expect(fetch).toHaveBeenCalledWith(
      expect.stringMatching(new RegExp(`/api/v1/projects/${PROJECT_ID}/images/${IMAGE_ID}/preview$`)),
      expect.objectContaining({ method: "POST" }),
    );
    expect(screen.getByText("128.0")).toBeInTheDocument();
    expect(screen.getByText("1")).toBeInTheDocument(); // versión de configuración
  });

  it("Restablecer valores está deshabilitado en los valores por defecto y se habilita tras mover un slider", async () => {
    vi.stubGlobal("fetch", vi.fn(() => Promise.resolve(previewResponse())));

    renderPanel();
    await screen.findByText("Preprocesada");

    expect(screen.getByRole("button", { name: "Restablecer valores" })).toBeDisabled();

    fireEvent.change(screen.getByLabelText("Brillo"), { target: { value: "20" } });

    expect(screen.getByRole("button", { name: "Restablecer valores" })).toBeEnabled();
  });
});

describe("PreprocessPanel — debounce de sliders", () => {
  it("mover el slider de contraste actualiza el valor mostrado de inmediato y, recién tras el debounce, pide un preview nuevo", async () => {
    vi.useFakeTimers();
    const fetch = vi.fn(() => Promise.resolve(previewResponse()));
    vi.stubGlobal("fetch", fetch);

    renderPanel();
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0);
    });
    expect(fetch).toHaveBeenCalledTimes(1);

    fireEvent.change(screen.getByLabelText("Contraste"), { target: { value: "1.8" } });
    expect(screen.getByText("1.8")).toBeInTheDocument();
    expect(fetch).toHaveBeenCalledTimes(1); // todavía no pasó el debounce

    await act(async () => {
      await vi.advanceTimersByTimeAsync(500);
    });

    expect(fetch).toHaveBeenCalledTimes(2);
  });
});

describe("PreprocessPanel — errores controlados", () => {
  it("un error controlado de la Web API (por ejemplo, dimensiones excesivas) se muestra sin romper el panel", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(() => Promise.resolve(errorResponse(413, "dimensions_exceeded", "La imagen es demasiado grande."))),
    );

    renderPanel();

    expect(await screen.findByRole("alert")).toHaveTextContent("La imagen es demasiado grande.");
  });
});
