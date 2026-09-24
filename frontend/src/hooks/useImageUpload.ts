import { useCallback, useEffect, useRef, useState } from "react";
import { uploadProjectImage } from "../api/projectsApi";
import { ApiClientError } from "../api/httpClient";
import { validateImageFile } from "../lib/validateImageFile";
import type { ApiErrorResponse, UploadErrorCode, UploadImageResponse } from "../types/upload";

export type UploadStatus = "idle" | "invalid" | "selected" | "uploading" | "success" | "error";

export interface UseImageUploadState {
  status: UploadStatus;
  file: File | null;
  previewUrl: string | null;
  progress: number;
  errorCode: UploadErrorCode | null;
  errorMessage: string | null;
  result: UploadImageResponse | null;
  selectFile: (file: File) => void;
  confirm: () => void;
  cancel: () => void;
  reset: () => void;
}

const GENERIC_ERROR_MESSAGE = "No se pudo completar la carga. Intentá de nuevo.";

function isKnownErrorCode(code: string): code is UploadErrorCode {
  return [
    "empty_file",
    "file_too_large",
    "unsupported_format",
    "corrupt_file",
    "upload_interrupted",
    "storage_failure",
    "not_found",
    "internal_error",
  ].includes(code);
}

/**
 * Orquesta el flujo completo de carga de imagen (Dropzone -> preview -> confirmar
 * -> progreso -> proyecto creado) descrito en spec.md. La validación de cliente es
 * solo UX; la Web API vuelve a validar todo antes de guardar nada.
 */
export function useImageUpload(): UseImageUploadState {
  const [status, setStatus] = useState<UploadStatus>("idle");
  const [file, setFile] = useState<File | null>(null);
  const [previewUrl, setPreviewUrl] = useState<string | null>(null);
  const [progress, setProgress] = useState(0);
  const [errorCode, setErrorCode] = useState<UploadErrorCode | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [result, setResult] = useState<UploadImageResponse | null>(null);

  const abortControllerRef = useRef<AbortController | null>(null);

  // El object URL de preview se libera cada vez que cambia o al desmontar, para no
  // filtrar memoria mientras el usuario prueba varios archivos.
  useEffect(() => {
    return () => {
      if (previewUrl) {
        URL.revokeObjectURL(previewUrl);
      }
    };
  }, [previewUrl]);

  const selectFile = useCallback((selected: File) => {
    setFile(selected);
    setPreviewUrl((prev) => {
      if (prev) URL.revokeObjectURL(prev);
      return URL.createObjectURL(selected);
    });
    setResult(null);
    setProgress(0);

    const validation = validateImageFile(selected);
    if (validation.ok) {
      setStatus("selected");
      setErrorCode(null);
      setErrorMessage(null);
    } else {
      setStatus("invalid");
      setErrorCode(validation.code ?? "unsupported_format");
      setErrorMessage(validation.message ?? GENERIC_ERROR_MESSAGE);
    }
  }, []);

  const reset = useCallback(() => {
    abortControllerRef.current?.abort();
    abortControllerRef.current = null;
    setStatus("idle");
    setFile(null);
    setPreviewUrl((prev) => {
      if (prev) URL.revokeObjectURL(prev);
      return null;
    });
    setProgress(0);
    setErrorCode(null);
    setErrorMessage(null);
    setResult(null);
  }, []);

  const cancel = useCallback(() => {
    if (status === "uploading") {
      abortControllerRef.current?.abort();
      abortControllerRef.current = null;
    }
    reset();
  }, [status, reset]);

  const confirm = useCallback(() => {
    if (!file || (status !== "selected" && status !== "error")) {
      return;
    }

    const controller = new AbortController();
    abortControllerRef.current = controller;
    setStatus("uploading");
    setProgress(0);
    setErrorCode(null);
    setErrorMessage(null);

    uploadProjectImage(file, {
      signal: controller.signal,
      onProgress: setProgress,
    })
      .then((response) => {
        abortControllerRef.current = null;
        setResult(response);
        setStatus("success");
      })
      .catch((error: unknown) => {
        abortControllerRef.current = null;
        if (error instanceof ApiClientError && error.isAborted) {
          // Cancelado explícitamente: reset() ya dejó el estado en "idle".
          return;
        }

        if (error instanceof ApiClientError && error.body) {
          const body = error.body as Partial<ApiErrorResponse>;
          const code = typeof body.code === "string" && isKnownErrorCode(body.code) ? body.code : "internal_error";
          setErrorCode(code);
          setErrorMessage(body.message ?? GENERIC_ERROR_MESSAGE);
        } else if (error instanceof ApiClientError && error.isNetworkError) {
          setErrorCode("upload_interrupted");
          setErrorMessage("La carga se interrumpió: no se pudo contactar a la Web API. Intentá de nuevo.");
        } else {
          setErrorCode("internal_error");
          setErrorMessage(GENERIC_ERROR_MESSAGE);
        }
        setStatus("error");
      });
  }, [file, status]);

  return { status, file, previewUrl, progress, errorCode, errorMessage, result, selectFile, confirm, cancel, reset };
}
