/**
 * Cliente HTTP centralizado hacia ASP.NET Core. Todo llamado del frontend a la
 * Web API pasa por acá — el navegador nunca llama directamente a Python.
 */

export const API_BASE_URL: string = import.meta.env.VITE_API_BASE_URL ?? "http://localhost:5080";

export class ApiClientError extends Error {
  readonly cause?: unknown;
  /** Cuerpo de error tipado (ApiErrorResponse) cuando la Web API respondió 4xx/5xx con JSON. */
  readonly body?: unknown;
  /** true si nunca se pudo contactar a la Web API (red caída, CORS). */
  readonly isNetworkError: boolean;
  /** true si la operación fue cancelada explícitamente (botón "Cancelar" o AbortSignal). */
  readonly isAborted: boolean;

  constructor(
    message: string,
    options?: { cause?: unknown; body?: unknown; isNetworkError?: boolean; isAborted?: boolean },
  ) {
    super(message);
    this.name = "ApiClientError";
    this.cause = options?.cause;
    this.body = options?.body;
    this.isNetworkError = options?.isNetworkError ?? false;
    this.isAborted = options?.isAborted ?? false;
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  let response: Response;

  try {
    response = await fetch(`${API_BASE_URL}${path}`, {
      headers: { Accept: "application/json" },
      ...init,
    });
  } catch (error) {
    throw new ApiClientError(
      `No se pudo contactar a la Web API en ${API_BASE_URL}${path}`,
      { cause: error, isNetworkError: true },
    );
  }

  if (!response.ok) {
    const body = await response.json().catch(() => undefined);
    throw new ApiClientError(
      `La Web API respondió con código ${response.status} en ${path}`,
      { body },
    );
  }

  return (await response.json()) as T;
}

export const httpClient = {
  get: <T>(path: string, init?: RequestInit) => request<T>(path, { ...init, method: "GET" }),
};

export interface UploadFileOptions {
  /** Nombre del campo multipart que espera el endpoint. Default: "file". */
  fieldName?: string;
  headers?: Record<string, string>;
  onProgress?: (percent: number) => void;
  signal?: AbortSignal;
}

function safeParseJson(text: string): unknown {
  if (!text) {
    return undefined;
  }
  try {
    return JSON.parse(text) as unknown;
  } catch {
    return undefined;
  }
}

/**
 * Sube un archivo como multipart/form-data con progreso de subida. fetch no expone
 * eventos de progreso de upload, así que esta función usa XMLHttpRequest — el único
 * lugar del cliente HTTP que lo hace, todo lo demás pasa por `request`/fetch.
 */
export function uploadFile<T>(path: string, file: File, options?: UploadFileOptions): Promise<T> {
  const fieldName = options?.fieldName ?? "file";
  const url = `${API_BASE_URL}${path}`;

  return new Promise<T>((resolve, reject) => {
    if (options?.signal?.aborted) {
      reject(new ApiClientError("La carga fue cancelada.", { isAborted: true }));
      return;
    }

    const xhr = new XMLHttpRequest();
    const formData = new FormData();
    formData.append(fieldName, file);

    xhr.open("POST", url);
    xhr.setRequestHeader("Accept", "application/json");
    for (const [key, value] of Object.entries(options?.headers ?? {})) {
      xhr.setRequestHeader(key, value);
    }

    const onAbort = () => xhr.abort();
    options?.signal?.addEventListener("abort", onAbort);
    const cleanup = () => options?.signal?.removeEventListener("abort", onAbort);

    xhr.upload.onprogress = (event) => {
      if (event.lengthComputable) {
        options?.onProgress?.(Math.round((event.loaded / event.total) * 100));
      }
    };

    xhr.onload = () => {
      cleanup();
      const body = safeParseJson(xhr.responseText);
      if (xhr.status >= 200 && xhr.status < 300) {
        resolve(body as T);
      } else {
        reject(new ApiClientError(`La Web API respondió con código ${xhr.status} en ${path}`, { body }));
      }
    };

    xhr.onerror = () => {
      cleanup();
      reject(new ApiClientError(`No se pudo contactar a la Web API en ${url}`, { isNetworkError: true }));
    };

    xhr.onabort = () => {
      cleanup();
      reject(new ApiClientError("La carga fue cancelada.", { isAborted: true }));
    };

    xhr.send(formData);
  });
}
