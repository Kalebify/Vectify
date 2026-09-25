import type { CheckIssuePayload } from "../../types/check";

/** Filtro por tipo de issue -- criterio de aceptación explícito de spec.md M1-S08: "filtros (por tipo de issue como mínimo: abiertos vs duplicados)". */
export type IssueFilter = "all" | "open_path" | "duplicate_path";

interface CheckIssueListProps {
  issues: CheckIssuePayload[];
  filter: IssueFilter;
  onFilterChange: (filter: IssueFilter) => void;
  selectedIssueId: string | null;
  onSelectIssue: (issueId: string) => void;
}

const FILTER_OPTIONS: Array<{ value: IssueFilter; label: string }> = [
  { value: "all", label: "Todos" },
  { value: "open_path", label: "Abiertos" },
  { value: "duplicate_path", label: "Duplicados" },
];

const SEVERITY_LABEL: Record<CheckIssuePayload["severity"], string> = {
  error: "Error",
  warning: "Advertencia",
};

function describeIssue(issue: CheckIssuePayload): string {
  if (issue.type === "open_path") {
    return (
      `Path abierto que debería cerrarse — path #${issue.pathIndex}, subpath #${issue.subpathIndex} ` +
      `(separación entre extremos: ${issue.gapDistance.toFixed(3)} unidades del modelo).`
    );
  }

  const memberList = issue.members.map((member) => `#${member.pathIndex}`).join(", ");
  const kind = issue.exact ? "Duplicado exacto" : "Casi-duplicado";
  return `${kind} entre los paths ${memberList} (distancia máxima punto a punto: ${issue.maxPointDistance.toFixed(3)} unidades).`;
}

/**
 * Panel de issues del Laser Checker (spec.md M1-S08): lista con severidad,
 * filtro por tipo, y click-to-highlight -- cada issue es un `<button>` (no
 * un `<div>` con onClick) que notifica al padre para resaltar el path
 * correspondiente sobre VectorCanvas (ver CheckPanel/lib/highlightSvgPath).
 */
export function CheckIssueList({ issues, filter, onFilterChange, selectedIssueId, onSelectIssue }: CheckIssueListProps) {
  const filteredIssues = issues.filter((issue) => filter === "all" || issue.type === filter);

  return (
    <div className="check-issue-list">
      <div className="check-issue-list__filters" role="radiogroup" aria-label="Filtrar por tipo de problema">
        {FILTER_OPTIONS.map((option) => (
          <label key={option.value} className="check-issue-list__filter-option">
            <input
              type="radio"
              name="check-issue-filter"
              value={option.value}
              checked={filter === option.value}
              onChange={() => onFilterChange(option.value)}
              aria-label={option.label}
            />
            <span aria-hidden="true">{option.label}</span>
          </label>
        ))}
      </div>

      {filteredIssues.length === 0 ? (
        <p className="check-issue-list__empty">Sin problemas para este filtro.</p>
      ) : (
        <ul className="check-issue-list__items">
          {filteredIssues.map((issue) => (
            <li key={issue.id}>
              <button
                type="button"
                className={`check-issue-list__item check-issue-list__item--${issue.severity}`}
                onClick={() => onSelectIssue(issue.id)}
                aria-pressed={selectedIssueId === issue.id}
              >
                <span className={`check-issue-list__severity check-issue-list__severity--${issue.severity}`}>
                  {SEVERITY_LABEL[issue.severity]}
                </span>
                <span className="check-issue-list__description">{describeIssue(issue)}</span>
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
