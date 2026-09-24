/**
 * Cliente HTTP centralizado hacia ASP.NET Core. Todo llamado del frontend a la
 * Web API pasa por acá — el navegador nunca llama directamente a Python.
 */

const API_BASE_URL: string = import.meta.env.VITE_API_BASE_URL ?? "http://localhost:5080";

export class ApiClientError extends Error {
  readonly cause?: unknown;

  constructor(message: string, cause?: unknown) {
    super(message);
    this.name = "ApiClientError";
    this.cause = cause;
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
      error,
    );
  }

  if (!response.ok) {
    throw new ApiClientError(
      `La Web API respondió con código ${response.status} en ${path}`,
    );
  }

  return (await response.json()) as T;
}

export const httpClient = {
  get: <T>(path: string, init?: RequestInit) => request<T>(path, { ...init, method: "GET" }),
};
