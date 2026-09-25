import { API_BASE_URL } from "./httpClient";
import type { ExportSourceKind } from "../types/export";

/**
 * URL de descarga de GET .../export: un GET simple con
 * Content-Disposition: attachment -- basta con navegar a esta URL (un `<a
 * href download>` o `window.location`) para que el navegador descargue el
 * archivo con el nombre sugerido por el servidor, sin necesidad de leer la
 * respuesta con fetch. Ver Vectify.Api.Endpoints.ExportEndpoints.
 */
export function getExportSvgUrl(
  projectId: string,
  imageId: string,
  sourceKind: ExportSourceKind,
  sourceId: string,
): string {
  const params = new URLSearchParams({ sourceKind, sourceId });
  return `${API_BASE_URL}/api/v1/projects/${projectId}/images/${imageId}/export?${params.toString()}`;
}
