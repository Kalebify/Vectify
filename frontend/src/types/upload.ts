/**
 * Contratos tipados que expone ASP.NET Core para la carga de imágenes
 * (POST /api/v1/projects, GET /api/v1/projects/{id}/images/{id}/original).
 * Deben reflejar exactamente Vectify.Api.Contracts.UploadImageResponse /
 * ApiErrorResponse del backend.
 */

export type ProjectStatus = "uploaded";

export interface UploadImageResponse {
  projectId: string;
  imageId: string;
  filename: string;
  mimeType: string;
  bytes: number;
  width: number | null;
  height: number | null;
  status: ProjectStatus;
}

/**
 * Code es estable y se mapea a copy en React sin parsear message. Valores que
 * puede devolver la Web API para el endpoint de upload.
 */
export type UploadErrorCode =
  | "empty_file"
  | "file_too_large"
  | "unsupported_format"
  | "corrupt_file"
  | "upload_interrupted"
  | "storage_failure"
  | "not_found"
  | "internal_error"
  | "network_error";

export interface ApiErrorResponse {
  code: string;
  message: string;
}
