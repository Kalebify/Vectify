/**
 * Contratos tipados que expone ASP.NET Core para el Laser Checker de paths
 * abiertos/duplicados (M1-S08): POST .../check. Deben reflejar exactamente
 * Vectify.Api.Contracts.CheckRequest/CheckResponse/CheckIssuePayload.
 */

/** El spec no aclara si el checker opera sobre una VectorVersion (M1-S05) o también sobre una SimplificationVersion (M1-S07) -- se aceptan ambas. */
export type CheckSourceKind = "vector" | "simplification";

export type CheckIssueSeverity = "warning" | "error";

export interface CheckBoundsPayload {
  minX: number;
  minY: number;
  maxX: number;
  maxY: number;
}

export interface CheckSummaryPayload {
  openPathCount: number;
  duplicateGroupCount: number;
}

export interface CheckDuplicateMemberPayload {
  pathIndex: number;
  subpathIndex: number;
  bounds: CheckBoundsPayload;
}

/** Un subpath sin `Z` cuyo primer y último punto están dentro de la tolerancia -- severidad siempre "error". */
export interface OpenPathIssuePayload {
  type: "open_path";
  id: string;
  severity: CheckIssueSeverity;
  pathIndex: number;
  subpathIndex: number;
  startX: number;
  startY: number;
  endX: number;
  endY: number;
  gapDistance: number;
  bounds: CheckBoundsPayload;
}

/** Un grupo de 2+ subpaths iguales o casi-iguales -- "exact" distingue duplicado byte-a-byte ("error") de casi-idéntico ("warning"). */
export interface DuplicatePathIssuePayload {
  type: "duplicate_path";
  id: string;
  severity: CheckIssueSeverity;
  exact: boolean;
  maxPointDistance: number;
  members: CheckDuplicateMemberPayload[];
}

export type CheckIssuePayload = OpenPathIssuePayload | DuplicatePathIssuePayload;

/** Respuesta de POST .../check. Análisis de SOLO LECTURA: nunca incluye el SVG. */
export interface CheckResponse {
  projectId: string;
  imageId: string;
  sourceKind: CheckSourceKind;
  sourceId: string;
  summary: CheckSummaryPayload;
  issues: CheckIssuePayload[];
  skippedPathCount: number;
  closeGapRatio: number;
  duplicatePointRatio: number;
}

/**
 * Code es estable y se mapea a copy en React sin parsear message. Valores
 * que puede devolver POST .../check.
 */
export type CheckErrorCode =
  | "invalid_parameters"
  | "not_found"
  | "invalid_input_svg"
  | "svg_too_large"
  | "too_many_subpaths"
  | "timeout"
  | "engine_unavailable"
  | "invalid_response"
  | "processing_error"
  | "storage_failure"
  | "internal_error"
  | "network_error";
