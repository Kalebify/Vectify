import type { ManufacturingOperationValue } from "../../types/manufacturingOperations";

/**
 * Copy/leyenda compartida entre LayerList (selector + badge por fila) y
 * ManufacturingOperationSummary (filtro + resumen) -- en su propio módulo
 * (no un named export adicional de un archivo de componente) para que
 * Fast Refresh siga funcionando sin advertencias (oxlint
 * react(only-export-components)).
 */
export const OPERATION_LABEL: Record<ManufacturingOperationValue, string> = {
  cut: "Corte",
  engrave: "Grabado",
  ignore: "Ignorar",
  unassigned: "Sin asignar",
};
