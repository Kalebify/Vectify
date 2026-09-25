/**
 * Contratos tipados que expone ASP.NET Core para la exportación SVG
 * (M1-S10): GET .../export?sourceKind=...&sourceId=.... No hay un cuerpo
 * JSON de éxito (el endpoint devuelve directamente el SVG como archivo
 * descargable, ver Vectify.Api.Endpoints.ExportEndpoints) -- este archivo
 * solo refleja sourceKind y los códigos de error controlados.
 */

/**
 * M1-S10 extiende a un tercer valor el mismo patrón dual ya usado en
 * CheckSourceKind/DimensionSourceKind (M1-S08/M1-S09): se puede exportar un
 * SVG ya vectorizado, ya simplificado, o ya dimensionado en mm.
 */
export type ExportSourceKind = "vector" | "simplification" | "dimension";

/**
 * Code es estable y se mapea a copy en React sin parsear message. Valores
 * que puede devolver GET .../export.
 */
export type ExportErrorCode = "invalid_parameters" | "not_found" | "network_error";
