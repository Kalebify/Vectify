import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { CheckPanel, type CheckSourceOption } from "./CheckPanel";

const PROJECT_ID = "11111111-1111-1111-1111-111111111111";
const IMAGE_ID = "22222222-2222-2222-2222-222222222222";
const VECTOR_ID = "33333333-3333-3333-3333-333333333333";
const SIMPLIFICATION_ID = "44444444-4444-4444-4444-444444444444";

const VECTOR_SVG_URL = `/api/v1/projects/${PROJECT_ID}/images/${IMAGE_ID}/vectors/${VECTOR_ID}`;
const SIMPLIFICATION_SVG_URL = `/api/v1/projects/${PROJECT_ID}/images/${IMAGE_ID}/simplifications/${SIMPLIFICATION_ID}`;

const SOURCE_SVG_TEXT =
  '<svg xmlns="http://www.w3.org/2000/svg" width="10" height="10">' +
  '<path d="M0,0 L10,0 L10,10 L0,9.99"/>' +
  '<path d="M2,2 L4,2 L4,4 L2,4 Z"/>' +
  '<path d="M2,2 L4,2 L4,4 L2,4 Z"/>' +
  "</svg>";

const VECTOR_SOURCE: CheckSourceOption = {
  kind: "vector",
  id: VECTOR_ID,
  label: "Vector actual",
  svgUrl: VECTOR_SVG_URL,
  width: 10,
  height: 10,
};

const SIMPLIFICATION_SOURCE: CheckSourceOption = {
  kind: "simplification",
  id: SIMPLIFICATION_ID,
  label: "Última simplificación",
  svgUrl: SIMPLIFICATION_SVG_URL,
  width: 10,
  height: 10,
};

function checkResponse(overrides: Record<string, unknown> = {}): Response {
  return new Response(
    JSON.stringify({
      projectId: PROJECT_ID,
      imageId: IMAGE_ID,
      sourceKind: "vector",
      sourceId: VECTOR_ID,
      summary: { openPathCount: 1, duplicateGroupCount: 1 },
      issues: [
        {
          type: "open_path",
          id: "open-0-0",
          severity: "error",
          pathIndex: 0,
          subpathIndex: 0,
          startX: 0,
          startY: 0,
          endX: 0,
          endY: 9.99,
          gapDistance: 0.01,
          bounds: { minX: 0, minY: 0, maxX: 10, maxY: 10 },
        },
        {
          type: "duplicate_path",
          id: "dup-1",
          severity: "warning",
          exact: true,
          maxPointDistance: 0,
          members: [
            { pathIndex: 1, subpathIndex: 0, bounds: { minX: 2, minY: 2, maxX: 4, maxY: 4 } },
            { pathIndex: 2, subpathIndex: 0, bounds: { minX: 2, minY: 2, maxX: 4, maxY: 4 } },
          ],
        },
      ],
      skippedPathCount: 0,
      closeGapRatio: 0.005,
      duplicatePointRatio: 0.002,
      ...overrides,
    }),
    { status: 200, headers: { "Content-Type": "application/json" } },
  );
}

function svgTextResponse(): Response {
  return new Response(SOURCE_SVG_TEXT, { status: 200, headers: { "Content-Type": "image/svg+xml" } });
}

function errorResponse(status: number, code: string, message: string): Response {
  return new Response(JSON.stringify({ code, message }), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

function stubFetch(handlers: { onCheck?: () => Response; onSvg?: () => Response }) {
  const fetch = vi.fn((input: RequestInfo | URL, _init?: RequestInit) => {
    const url = input.toString();
    if (url.includes("/check")) {
      return Promise.resolve((handlers.onCheck ?? checkResponse)());
    }
    return Promise.resolve((handlers.onSvg ?? svgTextResponse)());
  });
  vi.stubGlobal("fetch", fetch);
  return fetch;
}

function renderPanel(sources: CheckSourceOption[] = [VECTOR_SOURCE]) {
  return render(
    <CheckPanel projectId={PROJECT_ID} imageId={IMAGE_ID} fileName="logo.png" sources={sources} />,
  );
}

describe("CheckPanel — estado inicial", () => {
  it("no pide nada al montar", () => {
    const fetch = stubFetch({});
    fetch.mockClear();

    renderPanel();

    expect(screen.getByRole("button", { name: "Analizar" })).toBeInTheDocument();
    expect(fetch).not.toHaveBeenCalled();
    expect(screen.queryByText("Paths abiertos")).not.toBeInTheDocument();
  });
});

describe("CheckPanel — ejecutar análisis", () => {
  it("al pulsar 'Analizar' pide el análisis y muestra los contadores de issues", async () => {
    const fetch = stubFetch({});

    renderPanel();
    fireEvent.click(screen.getByRole("button", { name: "Analizar" }));

    expect(await screen.findByText("Paths abiertos")).toBeInTheDocument();
    expect(screen.getAllByText("1", { selector: "dd" })).toHaveLength(2);
    expect(screen.getByText("Duplicados/casi-duplicados")).toBeInTheDocument();

    expect(fetch).toHaveBeenCalledWith(
      expect.stringMatching(new RegExp(`/api/v1/projects/${PROJECT_ID}/images/${IMAGE_ID}/check$`)),
      expect.objectContaining({ method: "POST" }),
    );
    const init = fetch.mock.calls.find((call) => call[0].toString().includes("/check"))![1] as RequestInit;
    expect(JSON.parse(init.body as string)).toEqual({
      sourceKind: "vector",
      sourceId: VECTOR_ID,
      closeGapRatio: null,
      duplicatePointRatio: null,
    });
  });

  it("lista ambos issues con su severidad", async () => {
    stubFetch({});

    renderPanel();
    fireEvent.click(screen.getByRole("button", { name: "Analizar" }));

    expect(await screen.findByText(/Path abierto que debería cerrarse/)).toBeInTheDocument();
    expect(screen.getByText(/Duplicado exacto/)).toBeInTheDocument();
    expect(screen.getByText("Error")).toBeInTheDocument();
    expect(screen.getByText("Advertencia")).toBeInTheDocument();
  });
});

describe("CheckPanel — filtro por tipo", () => {
  it("filtrar por 'Abiertos' oculta los issues de duplicados", async () => {
    stubFetch({});

    renderPanel();
    fireEvent.click(screen.getByRole("button", { name: "Analizar" }));
    await screen.findByText(/Path abierto que debería cerrarse/);

    fireEvent.click(screen.getByRole("radio", { name: "Abiertos" }));

    expect(screen.getByText(/Path abierto que debería cerrarse/)).toBeInTheDocument();
    expect(screen.queryByText(/Duplicado exacto/)).not.toBeInTheDocument();
  });

  it("filtrar por 'Duplicados' oculta los issues de paths abiertos", async () => {
    stubFetch({});

    renderPanel();
    fireEvent.click(screen.getByRole("button", { name: "Analizar" }));
    await screen.findByText(/Duplicado exacto/);

    fireEvent.click(screen.getByRole("radio", { name: "Duplicados" }));

    expect(screen.queryByText(/Path abierto que debería cerrarse/)).not.toBeInTheDocument();
    expect(screen.getByText(/Duplicado exacto/)).toBeInTheDocument();
  });
});

describe("CheckPanel — click-to-highlight", () => {
  it("hacer click en un issue descarga el SVG y lo resalta en el canvas", async () => {
    const fetch = stubFetch({});

    renderPanel();
    fireEvent.click(screen.getByRole("button", { name: "Analizar" }));
    await screen.findByText(/Path abierto que debería cerrarse/);

    // El texto del SVG se descarga automáticamente al quedar listo el
    // análisis (una sola vez, no en cada click de issue).
    await waitFor(() =>
      expect(fetch).toHaveBeenCalledWith(VECTOR_SVG_URL, expect.anything()),
    );

    const issueButton = screen.getByText(/Path abierto que debería cerrarse/).closest("button")!;
    fireEvent.click(issueButton);

    await waitFor(() => expect(issueButton).toHaveAttribute("aria-pressed", "true"));

    const canvasImage = await screen.findByRole("img", { name: /con el problema seleccionado resaltado/ });
    expect(canvasImage.getAttribute("src")).toContain("data:image/svg+xml");

    // Click de nuevo deselecciona.
    fireEvent.click(issueButton);
    expect(issueButton).toHaveAttribute("aria-pressed", "false");
  });
});

describe("CheckPanel — selección de fuente", () => {
  it("muestra el selector de fuente solo si hay una simplificación disponible", () => {
    stubFetch({});
    renderPanel();

    expect(screen.queryByRole("radiogroup", { name: "Fuente a analizar" })).not.toBeInTheDocument();
  });

  it("con una simplificación disponible, cambiar de fuente reinicia el resultado y exige volver a analizar", async () => {
    stubFetch({});
    renderPanel([VECTOR_SOURCE, SIMPLIFICATION_SOURCE]);

    // Por defecto se selecciona la fuente más refinada (la última: la simplificación).
    expect(screen.getByRole("radio", { name: "Última simplificación" })).toBeChecked();

    fireEvent.click(screen.getByRole("button", { name: "Analizar" }));
    await screen.findByText("Paths abiertos");

    fireEvent.click(screen.getByRole("radio", { name: "Vector actual" }));

    expect(screen.queryByText("Paths abiertos")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Analizar" })).toBeInTheDocument();
  });
});

describe("CheckPanel — errores controlados", () => {
  it("un error controlado de la Web API se muestra sin romper el panel", async () => {
    stubFetch({ onCheck: () => errorResponse(404, "not_found", "No existe un SVG con ese ID.") });

    renderPanel();
    fireEvent.click(screen.getByRole("button", { name: "Analizar" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("No existe un SVG con ese ID.");
    expect(screen.getByRole("button", { name: "Analizar" })).toBeEnabled();
  });
});
