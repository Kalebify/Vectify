import { describe, expect, it } from "vitest";
import { MAX_FILE_SIZE_BYTES, validateImageFile } from "./validateImageFile";

function makeFile(name: string, type: string, size: number): File {
  const file = new File([new Uint8Array(Math.min(size, 1024))], name, { type });
  Object.defineProperty(file, "size", { value: size });
  return file;
}

describe("validateImageFile", () => {
  it("acepta un PNG válido", () => {
    const result = validateImageFile(makeFile("logo.png", "image/png", 2048));
    expect(result.ok).toBe(true);
  });

  it("acepta un JPG válido", () => {
    const result = validateImageFile(makeFile("logo.jpg", "image/jpeg", 2048));
    expect(result.ok).toBe(true);
  });

  it("acepta un WEBP válido", () => {
    const result = validateImageFile(makeFile("logo.webp", "image/webp", 2048));
    expect(result.ok).toBe(true);
  });

  it("rechaza un archivo vacío", () => {
    const result = validateImageFile(makeFile("vacio.png", "image/png", 0));
    expect(result.ok).toBe(false);
    expect(result.code).toBe("empty_file");
  });

  it("rechaza un archivo que supera el tamaño máximo", () => {
    const result = validateImageFile(makeFile("grande.png", "image/png", MAX_FILE_SIZE_BYTES + 1));
    expect(result.ok).toBe(false);
    expect(result.code).toBe("file_too_large");
  });

  it("rechaza un formato no soportado", () => {
    const result = validateImageFile(makeFile("documento.pdf", "application/pdf", 2048));
    expect(result.ok).toBe(false);
    expect(result.code).toBe("unsupported_format");
  });

  it("rechaza cuando la extensión no coincide con el Content-Type declarado", () => {
    const result = validateImageFile(makeFile("logo.txt", "image/png", 2048));
    expect(result.ok).toBe(false);
    expect(result.code).toBe("unsupported_format");
  });
});
