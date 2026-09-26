import { OPERATION_LABEL } from "./LayerList";
import type { ManufacturingOperationSummaryPayload, ManufacturingOperationValue } from "../../types/manufacturingOperations";

interface ManufacturingOperationSummaryProps {
  summary: ManufacturingOperationSummaryPayload;
  filter: ManufacturingOperationValue | "all";
  onChangeFilter: (filter: ManufacturingOperationValue | "all") => void;
}

const FILTER_OPTIONS: Array<ManufacturingOperationValue | "all"> = ["all", "cut", "engrave", "ignore", "unassigned"];

/**
 * Filtro por operación ("mostrar solo Corte") + resumen "N en Corte, M en
 * Grabado, K ignoradas, J sin asignar" de spec.md M2-S07 -- pensado para
 * verse "antes de exportar": se renderiza dentro del panel Layers ya
 * existente (no un panel paralelo), lo más cerca posible del final del
 * flujo de esta tarjeta, ya que el propio Export (M1-S10) opera sobre el
 * pipeline de un solo vector de MVP1 y es ajeno a paletteId/capas de color
 * (ver IMPL.md, "Ambigüedades resueltas": no se modifica ExportPanel/el
 * endpoint de exportación real, la propia tarjeta lo deja como trabajo
 * futuro).
 */
export function ManufacturingOperationSummary({ summary, filter, onChangeFilter }: ManufacturingOperationSummaryProps) {
  return (
    <div className="manufacturing-operation-summary">
      <fieldset className="manufacturing-operation-summary__filter">
        <legend className="manufacturing-operation-summary__filter-legend">Filtrar por operación</legend>
        <div className="manufacturing-operation-summary__filter-options" role="radiogroup" aria-label="Filtrar por operación">
          {FILTER_OPTIONS.map((option) => (
            <label key={option} className="manufacturing-operation-summary__filter-option">
              <input
                type="radio"
                name="manufacturing-operation-filter"
                checked={filter === option}
                onChange={() => onChangeFilter(option)}
                aria-label={option === "all" ? "Mostrar todas las capas" : `Mostrar solo ${OPERATION_LABEL[option]}`}
              />
              <span aria-hidden="true">{option === "all" ? "Todas" : OPERATION_LABEL[option]}</span>
            </label>
          ))}
        </div>
      </fieldset>

      <p className="manufacturing-operation-summary__text" role="status">
        {summary.cutCount} en Corte, {summary.engraveCount} en Grabado, {summary.ignoreCount} ignorada
        {summary.ignoreCount === 1 ? "" : "s"}, {summary.unassignedCount} sin asignar (de {summary.totalCount} capa
        {summary.totalCount === 1 ? "" : "s"} en total).
      </p>
    </div>
  );
}
