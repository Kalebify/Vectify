import { useMemo, useState } from "react";
import { LayerInfoPanel } from "../layers/LayerInfoPanel";
import { ManufacturingOperationSummary } from "../layers/ManufacturingOperationSummary";
import type { VectorDocumentLayer } from "../../hooks/useVectorDocument";
import type { ManufacturingOperationSummaryPayload, ManufacturingOperationValue } from "../../types/manufacturingOperations";

interface InspectorPanelProps {
  layers: VectorDocumentLayer[];
  selectedLayer: VectorDocumentLayer | null;
  isVisible: boolean;
  onIsolate: () => void;
  onShowAll: () => void;
  onRefresh: () => void;
}

function summarize(layers: VectorDocumentLayer[]): ManufacturingOperationSummaryPayload {
  return {
    cutCount: layers.filter((l) => l.manufacturingOperation === "cut").length,
    engraveCount: layers.filter((l) => l.manufacturingOperation === "engrave").length,
    ignoreCount: layers.filter((l) => l.manufacturingOperation === "ignore").length,
    unassignedCount: layers.filter((l) => l.manufacturingOperation === "unassigned").length,
    totalCount: layers.length,
  };
}

/**
 * "INSPECTOR" del wireframe obligatorio (columna derecha: "Color / Paths /
 * Pieces / Operation"): envuelve el `LayerInfoPanel` YA EXISTENTE
 * (M2.1-S03/M2.1-S04, exactamente esos mismos 4 campos + HEX/visibilidad) en
 * vez de reescribir esa lógica, adaptando sus props al `VectorDocument`
 * agregado de esta tarjeta -- ver `useVectorDocument`.
 *
 * Suma el resumen de operaciones de fabricación (`ManufacturingOperationSummary`,
 * M2-S07/MVP2) con un filtro puramente local a este panel (no afecta
 * `visibility` del canvas): el propio spec.md del M2-S07 documenta que este
 * resumen "se renderiza dentro del panel Layers/Inspector ya existente, no
 * un panel paralelo".
 *
 * `onRefresh` dispara un reload COMPLETO del VectorDocument (no un refetch
 * parcial del consolidado): a diferencia de `LayersPanel` clásico (que tiene
 * un `useConsolidatedVectorLayers` propio con `refetch` independiente), acá
 * hay una única fuente de verdad (`useVectorDocument`) y un único mecanismo
 * de refresco -- decisión documentada en IMPL.md.
 */
export function InspectorPanel({ layers, selectedLayer, isVisible, onIsolate, onShowAll, onRefresh }: InspectorPanelProps) {
  const [operationFilter, setOperationFilter] = useState<ManufacturingOperationValue | "all">("all");
  const summary = useMemo(() => summarize(layers), [layers]);

  return (
    <section aria-labelledby="inspector-heading" className="inspector-panel">
      <h3 id="inspector-heading" className="editor-panel__heading">
        Inspector
      </h3>

      <LayerInfoPanel
        selectedLayer={
          selectedLayer
            ? {
                groupId: selectedLayer.groupId,
                name: selectedLayer.name,
                colorHex: selectedLayer.colorHex,
                areaPercent: selectedLayer.areaPercent,
                hasPartialAlpha: selectedLayer.hasPartialAlpha,
                vectorId: selectedLayer.vectorId,
                svgUrl: selectedLayer.svgUrl,
              }
            : null
        }
        consolidated={
          selectedLayer
            ? {
                id: selectedLayer.groupId,
                name: selectedLayer.name,
                colorHex: selectedLayer.colorHex,
                fill: selectedLayer.fill,
                vectorId: selectedLayer.vectorId,
                svgUrl: selectedLayer.svgUrl,
                pathCount: selectedLayer.pathCount,
                componentCount: selectedLayer.componentCount,
                manufacturingOperation: selectedLayer.manufacturingOperation,
                visible: isVisible,
                locked: false,
                order: selectedLayer.order,
                rasterValidation: {
                  ownMismatchRatio: 0,
                  ownMismatchTolerance: 0,
                  ownMismatchWithinTolerance: true,
                  contaminationRatio: 0,
                  contaminationTolerance: 0,
                  contaminationWithinTolerance: true,
                  warnings: [],
                },
              }
            : null
        }
        consolidatedStatus={layers.length > 0 ? "ready" : "idle"}
        consolidatedErrorMessage={null}
        onRefresh={onRefresh}
        isVisible={isVisible}
        onIsolate={onIsolate}
        onShowAll={onShowAll}
      />

      {layers.length > 0 && (
        <ManufacturingOperationSummary summary={summary} filter={operationFilter} onChangeFilter={setOperationFilter} />
      )}
    </section>
  );
}
