import { act, fireEvent, render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it } from "vitest";
import { UploadPanel } from "./UploadPanel";
import { FakeXMLHttpRequest, stubXMLHttpRequest } from "../../test/FakeXMLHttpRequest";

function pngFile(name = "logo.png"): File {
  return new File([new Uint8Array([1, 2, 3, 4])], name, { type: "image/png" });
}

function selectFile(file: File) {
  const input = document.querySelector('input[type="file"]') as HTMLInputElement;
  fireEvent.change(input, { target: { files: [file] } });
}

beforeEach(() => {
  stubXMLHttpRequest();
  // jsdom no implementa createObjectURL/revokeObjectURL.
  URL.createObjectURL = () => "blob:mock";
  URL.revokeObjectURL = () => {};
});

describe("UploadPanel — selección", () => {
  it("muestra el Dropzone en el estado inicial", () => {
    render(<UploadPanel />);
    expect(screen.getByText("Arrastrá una imagen acá o hacé clic para elegirla")).toBeInTheDocument();
  });

  it("al elegir un PNG válido muestra nombre/tamaño y las acciones de confirmar/cancelar", () => {
    render(<UploadPanel />);

    act(() => selectFile(pngFile("mi-logo.png")));

    expect(screen.getByText("mi-logo.png")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Confirmar carga" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Cancelar" })).toBeInTheDocument();
  });

  it("al elegir un formato no soportado muestra un error comprensible sin llamar a la Web API", () => {
    render(<UploadPanel />);
    const invalidFile = new File(["contenido"], "documento.pdf", { type: "application/pdf" });

    act(() => selectFile(invalidFile));

    expect(screen.getByRole("alert")).toHaveTextContent(/Formato no soportado/i);
    expect(screen.getByRole("button", { name: "Elegir otra imagen" })).toBeInTheDocument();
    expect(FakeXMLHttpRequest.instances).toHaveLength(0);
  });

  it("cancelar en el estado seleccionado vuelve al Dropzone", () => {
    render(<UploadPanel />);
    act(() => selectFile(pngFile()));

    act(() => fireEvent.click(screen.getByRole("button", { name: "Cancelar" })));

    expect(screen.getByText("Arrastrá una imagen acá o hacé clic para elegirla")).toBeInTheDocument();
  });
});

describe("UploadPanel — confirmar carga", () => {
  it("confirmar envía la imagen y muestra progreso y el proyecto creado al completarse", async () => {
    render(<UploadPanel />);
    act(() => selectFile(pngFile("mi-logo.png")));
    act(() => fireEvent.click(screen.getByRole("button", { name: "Confirmar carga" })));

    expect(FakeXMLHttpRequest.instances).toHaveLength(1);
    const xhr = FakeXMLHttpRequest.latest();
    expect(xhr.url).toMatch(/\/api\/v1\/projects$/);

    act(() => xhr.progress(50, 100));
    expect(screen.getByText("50%")).toBeInTheDocument();

    await act(async () => {
      xhr.respond(201, {
        projectId: "11111111-1111-1111-1111-111111111111",
        imageId: "22222222-2222-2222-2222-222222222222",
        filename: "mi-logo.png",
        mimeType: "image/png",
        bytes: 4,
        width: 1,
        height: 1,
        status: "uploaded",
      });
    });

    expect(await screen.findByText("Proyecto creado")).toBeInTheDocument();
    expect(screen.getByText("mi-logo.png")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Cargar otra imagen" })).toBeInTheDocument();
  });

  it("envía Idempotency-Key en la carga y la mantiene igual en un reintento del mismo archivo", async () => {
    render(<UploadPanel />);
    act(() => selectFile(pngFile()));
    act(() => fireEvent.click(screen.getByRole("button", { name: "Confirmar carga" })));

    const firstXhr = FakeXMLHttpRequest.latest();
    const firstKey = firstXhr.getRequestHeader("Idempotency-Key");
    expect(firstKey).toBeTruthy();

    await act(async () => {
      firstXhr.respond(400, { code: "corrupt_file", message: "El archivo parece estar corrupto." });
    });

    act(() => fireEvent.click(screen.getByRole("button", { name: "Reintentar" })));

    const secondXhr = FakeXMLHttpRequest.latest();
    expect(secondXhr.getRequestHeader("Idempotency-Key")).toBe(firstKey);
  });

  it("elegir un archivo nuevo renueva la Idempotency-Key", () => {
    render(<UploadPanel />);
    act(() => selectFile(pngFile("primero.png")));
    act(() => fireEvent.click(screen.getByRole("button", { name: "Confirmar carga" })));
    const firstKey = FakeXMLHttpRequest.latest().getRequestHeader("Idempotency-Key");

    act(() => fireEvent.click(screen.getByRole("button", { name: "Cancelar" })));
    act(() => selectFile(pngFile("segundo.png")));
    act(() => fireEvent.click(screen.getByRole("button", { name: "Confirmar carga" })));
    const secondKey = FakeXMLHttpRequest.latest().getRequestHeader("Idempotency-Key");

    expect(secondKey).toBeTruthy();
    expect(secondKey).not.toBe(firstKey);
  });

  it("cancelar durante la carga aborta la solicitud y vuelve al Dropzone", () => {
    render(<UploadPanel />);
    act(() => selectFile(pngFile()));
    act(() => fireEvent.click(screen.getByRole("button", { name: "Confirmar carga" })));

    const xhr = FakeXMLHttpRequest.latest();
    act(() => fireEvent.click(screen.getByRole("button", { name: "Cancelar" })));

    expect(xhr.aborted).toBe(true);
    expect(screen.getByText("Arrastrá una imagen acá o hacé clic para elegirla")).toBeInTheDocument();
  });

  it("un error controlado de la Web API muestra el mensaje y permite reintentar", async () => {
    render(<UploadPanel />);
    act(() => selectFile(pngFile()));
    act(() => fireEvent.click(screen.getByRole("button", { name: "Confirmar carga" })));

    const xhr = FakeXMLHttpRequest.latest();
    await act(async () => {
      xhr.respond(400, { code: "corrupt_file", message: "El archivo parece estar corrupto." });
    });

    expect(await screen.findByRole("alert")).toHaveTextContent("El archivo parece estar corrupto.");
    expect(screen.getByRole("button", { name: "Reintentar" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Elegir otra imagen" })).toBeInTheDocument();
  });

  it("un fallo de red durante la carga se muestra como carga interrumpida", async () => {
    render(<UploadPanel />);
    act(() => selectFile(pngFile()));
    act(() => fireEvent.click(screen.getByRole("button", { name: "Confirmar carga" })));

    const xhr = FakeXMLHttpRequest.latest();
    await act(async () => {
      xhr.networkError();
    });

    expect(await screen.findByRole("alert")).toHaveTextContent(/interrumpió/i);
  });
});
