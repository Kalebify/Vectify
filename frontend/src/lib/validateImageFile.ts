import type { UploadErrorCode } from "../types/upload";

/**
 * Validación UX en el cliente: espeja las reglas de Vectify.Api.Validation.ImageUploadValidator
 * (mismos valores por defecto que backend/Vectify.Api/appsettings.json: Upload:MaxFileSizeBytes /
 * Upload:AllowedContentTypes) para dar feedback inmediato sin esperar la respuesta HTTP.
 * La Web API vuelve a validar todo esto — esto es solo UX, nunca la fuente de verdad.
 */
export const MAX_FILE_SIZE_BYTES = 15 * 1024 * 1024;

const EXTENSIONS_BY_CONTENT_TYPE: Record<string, string[]> = {
  "image/png": [".png"],
  "image/jpeg": [".jpg", ".jpeg"],
  "image/webp": [".webp"],
};

export const ALLOWED_CONTENT_TYPES = Object.keys(EXTENSIONS_BY_CONTENT_TYPE);

export interface FileValidationResult {
  ok: boolean;
  code?: UploadErrorCode;
  message?: string;
}

function extensionOf(fileName: string): string {
  const dotIndex = fileName.lastIndexOf(".");
  return dotIndex === -1 ? "" : fileName.slice(dotIndex).toLowerCase();
}

export function validateImageFile(file: File): FileValidationResult {
  if (file.size === 0) {
    return { ok: false, code: "empty_file", message: "El archivo está vacío." };
  }

  if (file.size > MAX_FILE_SIZE_BYTES) {
    const maxMb = MAX_FILE_SIZE_BYTES / (1024 * 1024);
    return {
      ok: false,
      code: "file_too_large",
      message: `El archivo supera el tamaño máximo permitido (${maxMb} MB).`,
    };
  }

  const contentType = file.type.toLowerCase();
  const extension = extensionOf(file.name);
  const validExtensions = EXTENSIONS_BY_CONTENT_TYPE[contentType];

  if (!validExtensions || !validExtensions.includes(extension)) {
    return {
      ok: false,
      code: "unsupported_format",
      message: "Formato no soportado. Solo se aceptan imágenes PNG, JPG/JPEG o WEBP.",
    };
  }

  return { ok: true };
}
