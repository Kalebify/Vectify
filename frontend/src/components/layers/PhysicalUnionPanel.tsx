import type { PhysicalUnionPhase } from "../../hooks/usePhysicalUnion";
import type { PhysicalUnionPreviewResponse } from "../../types/physicalUnion";

interface PhysicalUnionPanelProps {
  layerName: string;
  phase: PhysicalUnionPhase;
  preview: PhysicalUnionPreviewResponse | null;
  errorMessage: string | null;
  onConfirm: () => void;
  onCancel: () => void;
}

const STRATEGY_LABEL: Record<PhysicalUnionPreviewResponse["strategy"], string> = {
  boolean_union: "unión booleana directa (las piezas ya se tocan/superponen)",
  bridge: "un puente de conexión recto (las piezas estaban separadas)",
  mixed: "unión booleana + puentes de conexión (selección de 3+ piezas)",
};

/**
 * Preview antes/después de "Unión física" (M2-S06): geometría REAL ya
 * calculada por el motor Python (nunca una aproximación visual), número de
 * componentes resultante, y confirmar/cancelar explícitos -- spec.md,
 * criterio de aceptación: "la operación no se persiste hasta que el usuario
 * confirma explícitamente; cancelar no genera ningún cambio". Distinto en
 * texto/color de cualquier UI de "Agrupar" (M2-S05): esto SÍ modifica
 * geometría real, se lo advierte explícitamente.
 */
export function PhysicalUnionPanel({ layerName, phase, preview, errorMessage, onConfirm, onCancel }: PhysicalUnionPanelProps) {
  if (phase === "previewing") {
    return (
      <div className="physical-union-panel" role="status" aria-label={`Calculando unión física de ${layerName}`}>
        Calculando el resultado real de la unión física…
      </div>
    );
  }

  if (errorMessage) {
    return (
      <div className="physical-union-panel physical-union-panel--error" role="alert">
        <p className="physical-union-panel__warning">⚠ La unión física NO fue posible: {errorMessage}</p>
        <p className="physical-union-panel__note">La capa de {layerName} sigue exactamente como estaba antes de intentarlo.</p>
        <button type="button" className="upload-actions__button" onClick={onCancel}>
          Entendido
        </button>
      </div>
    );
  }

  if (!preview) {
    return null;
  }

  const isConfirming = phase === "confirming";
  const previewImageSrc = `data:image/svg+xml;utf8,${encodeURIComponent(preview.svg)}`;

  return (
    <div className="physical-union-panel" aria-label={`Preview de unión física de ${layerName}`}>
      <p className="physical-union-panel__warning">
        Esta acción modifica geometría real: se creará una nueva versión de la capa (la actual NO se destruye).
      </p>

      <div className="physical-union-panel__preview">
        <img src={previewImageSrc} alt={`Resultado real de fusionar las piezas seleccionadas de ${layerName}`} width={120} height={120} />
      </div>

      <dl className="physical-union-panel__counts">
        <div>
          <dt>Antes</dt>
          <dd>{preview.componentCountBefore} pieza(s)</dd>
        </div>
        <div>
          <dt>Después</dt>
          <dd>{preview.componentCountAfter} pieza(s)</dd>
        </div>
        <div>
          <dt>Estrategia</dt>
          <dd>{STRATEGY_LABEL[preview.strategy]}</dd>
        </div>
      </dl>

      <div className="physical-union-panel__actions">
        <button type="button" className="upload-actions__button upload-actions__button--primary" disabled={isConfirming} onClick={onConfirm}>
          Confirmar unión física
        </button>
        <button type="button" className="upload-actions__button" disabled={isConfirming} onClick={onCancel}>
          Cancelar
        </button>
      </div>
    </div>
  );
}
