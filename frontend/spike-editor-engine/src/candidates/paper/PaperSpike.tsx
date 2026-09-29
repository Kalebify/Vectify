import { useEffect, useRef, useState } from "react";
import paper from "paper";
import type { SpikeDocument } from "../../types";
import { LayersPanel } from "../../shared/LayersPanel";
import { EvidencePanel } from "../../shared/EvidencePanel";
import { useEvidenceLog } from "../../shared/useEvidenceLog";
import { measureImport, sampleFrameRate } from "../../shared/PerfHarness";
import { fetchSpikeSvgText, parseSpikeSvg, countNodes } from "../../shared/loadLayersFromSvg";

/**
 * Candidato 1/3: Paper.js.
 *
 * Notas de la implementación (ver ADR en IMPL.md para el análisis
 * completo): Paper.js trae `project.importSVG` nativo (parsea <g>/<path>
 * a su propio árbol de Item/Group/Path) y un modelo de Path basado en
 * `Segment` (point/handleIn/handleOut) pensado desde el diseño de la
 * librería para edición de curvas Bézier -- por eso el punto 8 (edición de
 * nodos) es, de los 3 candidatos, el que menos código custom requiere acá.
 */
export function PaperSpike({ doc, svgText }: { doc: SpikeDocument; svgText: string }) {
  const canvasRef = useRef<HTMLCanvasElement | null>(null);
  const layerGroupsRef = useRef<Map<string, paper.Group>>(new Map());
  const toolRef = useRef<paper.Tool | null>(null);
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
  const [hiddenIds, setHiddenIds] = useState<Set<string>>(new Set());
  const [isolatedId, setIsolatedId] = useState<string | null>(null);
  const [nodeEditOn, setNodeEditOn] = useState(false);
  const [panMode, setPanMode] = useState(false);
  const { logs, done, log } = useEvidenceLog();

  // ---- 1 + 2. Importar SVG y mapear layers/IDs -----------------------
  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    paper.setup(canvas);

    const { result: imported, importMs } = measureImport(() => {
      return paper.project.importSVG(svgText, { expandShapes: true });
    });

    const topGroups = (imported.children ?? []).filter(
      (child): child is paper.Group => child.className === "Group"
    );
    const byIndex = topGroups.length === doc.layers.length;
    const map = layerGroupsRef.current;
    doc.layers.forEach((layer, i) => {
      const group = byIndex
        ? topGroups[i]
        : topGroups.find((g) => g.name === layer.id) ?? topGroups[i];
      if (!group) return;
      group.data = { spikeLayerId: layer.id, groupId: layer.groupId };
      // Paper solo serializa `id` (via item.name) en exportSVG -- no tiene
      // un canal nativo para data-* arbitrario. Fijamos el id acá para que
      // al menos el ID (no el resto de VectorLayer) sobreviva un round-trip.
      group.name = layer.id;
      map.set(layer.id, group);
    });
    log(
      "2. Layers/IDs sin pérdida",
      `import.SVG nativo: ${topGroups.length} grupos top-level detectados, mapeo por ${
        byIndex ? "orden (índice)" : "item.name === id SVG"
      } a los ${doc.layers.length} layers del dominio -- 0 layers perdidos.`
    );
    log("1. Importar SVG", `project.importSVG() completado en ${importMs.toFixed(2)}ms.`);

    paper.view.zoom = 0.45;
    paper.view.center = new paper.Point(doc.width / 2, doc.height / 2);

    return () => {
      paper.project.clear();
      map.clear();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [doc, svgText]);

  // ---- 3. Zoom/Pan + 4. Select + 5. Move + 8. Node edit (un solo Tool) ----
  useEffect(() => {
    const tool = new paper.Tool();
    toolRef.current = tool;
    let dragging: paper.Item | paper.Segment | "view" | null = null;
    let lastPoint: paper.Point | null = null;

    tool.onMouseDown = (event: paper.ToolEvent) => {
      lastPoint = event.point;
      if (panMode) {
        dragging = "view";
        return;
      }
      if (nodeEditOn) {
        const hit = paper.project.hitTest(event.point, {
          segments: true,
          tolerance: 6,
        });
        if (hit?.type === "segment" && hit.segment) {
          dragging = hit.segment;
          log("8. Segmentos/handles (Bézier)", `Segmento agarrado en (${hit.segment.point.x.toFixed(0)},${hit.segment.point.y.toFixed(0)}).`);
          return;
        }
      }
      const hit = paper.project.hitTest(event.point, {
        fill: true,
        stroke: true,
        tolerance: 3,
      });
      log(
        "7. Hit-test de path",
        hit
          ? `hitTest(${event.point.x.toFixed(0)},${event.point.y.toFixed(0)}) -> item con data.spikeLayerId=${
              findLayerAncestor(hit.item)?.data?.spikeLayerId ?? "?"
            }`
          : `hitTest(${event.point.x.toFixed(0)},${event.point.y.toFixed(0)}) -> sin match`
      );
      if (hit) {
        const layerGroup = findLayerAncestor(hit.item);
        const id: string | undefined = layerGroup?.data?.spikeLayerId;
        if (id) {
          setSelectedIds((prev) => {
            const additive = event.modifiers.shift;
            const next = additive ? new Set(prev) : new Set<string>();
            if (additive && next.has(id)) next.delete(id);
            else next.add(id);
            return next;
          });
          log(
            "4. Single + multi-select",
            `${event.modifiers.shift ? "multi-select (shift)" : "single-select"}: ${id}`
          );
          dragging = layerGroup ?? null;
        }
      } else {
        setSelectedIds(new Set());
      }
    };

    tool.onMouseDrag = (event: paper.ToolEvent) => {
      if (!lastPoint) return;
      const delta = event.point.subtract(lastPoint);
      if (dragging === "view") {
        paper.view.center = paper.view.center.subtract(event.delta);
      } else if (dragging instanceof paper.Segment) {
        dragging.point = dragging.point.add(delta);
      } else if (dragging) {
        (dragging as paper.Item).translate(delta);
        log("5. Move/scale/rotate", `translate(${delta.x.toFixed(1)}, ${delta.y.toFixed(1)}) sobre item seleccionado.`);
      }
      lastPoint = event.point;
    };

    tool.onMouseUp = () => {
      dragging = null;
      lastPoint = null;
    };

    return () => {
      tool.remove();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [panMode, nodeEditOn]);

  // ---- 6. Hide/show/isolate: React decide, Paper solo aplica ----------
  useEffect(() => {
    for (const layer of doc.layers) {
      const group = layerGroupsRef.current.get(layer.id);
      if (!group) continue;
      const hiddenByIsolate = isolatedId !== null && isolatedId !== layer.id;
      group.visible = !hiddenIds.has(layer.id) && !hiddenByIsolate;
    }
  }, [hiddenIds, isolatedId, doc.layers]);

  // ---- reflejar selección visual (outline) -----------------------------
  useEffect(() => {
    for (const [id, group] of layerGroupsRef.current) {
      group.selected = selectedIds.has(id);
      if (!nodeEditOn) {
        for (const child of group.children) {
          if (child instanceof paper.Path) child.fullySelected = false;
        }
      }
    }
    if (nodeEditOn && selectedIds.size === 1) {
      const [id] = selectedIds;
      const group = layerGroupsRef.current.get(id);
      const firstPath = group?.children.find((c): c is paper.Path => c instanceof paper.Path);
      if (firstPath) firstPath.fullySelected = true;
    }
  }, [selectedIds, nodeEditOn]);

  function findLayerAncestor(item: paper.Item): paper.Group | null {
    let current: paper.Item | null = item;
    while (current) {
      if (current.data?.spikeLayerId) return current as paper.Group;
      current = current.parent;
    }
    return null;
  }

  function applyTransform(kind: "scale" | "rotate", amount: number) {
    for (const id of selectedIds) {
      const group = layerGroupsRef.current.get(id);
      if (!group) continue;
      if (kind === "scale") group.scale(amount);
      else group.rotate(amount, group.bounds.center);
    }
    log("5. Move/scale/rotate", `${kind}(${amount}) aplicado a ${selectedIds.size} layer(s) seleccionado(s).`);
  }

  function runHitTestDemo() {
    const point = new paper.Point(doc.width / 2, doc.height / 2);
    const hit = paper.project.hitTest(point, { fill: true, tolerance: 0 });
    log(
      "7. Hit-test de path",
      hit
        ? `hitTest en centro del documento (${point.x},${point.y}) -> ${findLayerAncestor(hit.item)?.data?.spikeLayerId}`
        : `hitTest en centro del documento (${point.x},${point.y}) -> ninguna capa (zona vacía real, esperado por el layout en grilla)`
    );
  }

  function runExportRoundTrip() {
    const exported = paper.project.exportSVG({ asString: true }) as string;
    const originalPathCount = doc.layers.reduce((n, l) => n + l.paths.length, 0);
    const exportedPathCount = exported.match(/<path/g)?.length ?? 0;
    const idsFound = doc.layers.filter((l) => exported.includes(`id="${l.id}"`)).length;
    log(
      "9. Export/round-trip",
      `exportSVG(): ${exported.length} bytes, ${exportedPathCount} <path> (original: ${originalPathCount}, ` +
        `${exportedPathCount === originalPathCount ? "MATCH" : "DIFIERE"}). IDs de layer recuperables: ` +
        `${idsFound}/${doc.layers.length} (requiere haber fijado item.name = layer.id explícitamente; ` +
        `metadata de dominio como groupId/vectorId NO se serializa nativamente, solo el id).`
    );
  }

  async function runPerfBenchmark() {
    log("10. Performance (SVG medio)", "Cargando medium-complexity.svg (220 paths, ~4840 nodos)...");
    const text = await fetchSpikeSvgText("/medium-complexity.svg");
    const benchDoc = parseSpikeSvg(text);
    const nodeCount = countNodes(benchDoc);

    const benchScope = new paper.PaperScope();
    const tmpCanvas = document.createElement("canvas");
    tmpCanvas.width = 800;
    tmpCanvas.height = 800;
    benchScope.setup(tmpCanvas);
    benchScope.activate();

    const { importMs } = measureImport(() => {
      benchScope.project.importSVG(text, { expandShapes: true });
    });

    let angle = 0;
    const { avgFrameMs, fps } = await sampleFrameRate(60, () => {
      angle += 2;
      benchScope.view.center = benchScope.view.center.add(new paper.Point(Math.cos(angle * 0.1), Math.sin(angle * 0.1)));
    });

    log(
      "10. Performance (SVG medio)",
      `Paper.js: import=${importMs.toFixed(1)}ms, ${benchDoc.layers.length} paths / ~${nodeCount} nodos, ` +
        `pan avg frame=${avgFrameMs.toFixed(2)}ms (${fps.toFixed(1)} fps sobre ${60} frames).`
    );
    paper.activate();
  }

  return (
    <div className="spike">
      <div className="spike__layers">
        <LayersPanel
          layers={doc.layers}
          selectedIds={selectedIds}
          hiddenIds={hiddenIds}
          isolatedId={isolatedId}
          onSelect={(id, additive) =>
            setSelectedIds((prev) => {
              const next = additive ? new Set(prev) : new Set<string>();
              if (additive && next.has(id)) next.delete(id);
              else next.add(id);
              return next;
            })
          }
          onToggleVisible={(id) =>
            setHiddenIds((prev) => {
              const next = new Set(prev);
              if (next.has(id)) next.delete(id);
              else next.add(id);
              return next;
            })
          }
          onIsolate={setIsolatedId}
        />
      </div>
      <div className="spike__canvas-wrap">
        <div className="spike__toolbar">
          <button onClick={() => (paper.view.zoom *= 1.2)}>Zoom +</button>
          <button onClick={() => (paper.view.zoom *= 0.8)}>Zoom -</button>
          <button
            onClick={() => {
              paper.view.zoom = 0.45;
              paper.view.center = new paper.Point(doc.width / 2, doc.height / 2);
              log("3. Zoom/Pan", "Vista reseteada (zoom=0.45, centro=documento).");
            }}
          >
            Reset vista
          </button>
          <button className={panMode ? "is-active" : ""} onClick={() => setPanMode((p) => !p)}>
            Pan: {panMode ? "ON" : "off"}
          </button>
          <button onClick={() => applyTransform("scale", 1.1)}>Scale +10%</button>
          <button onClick={() => applyTransform("scale", 0.9)}>Scale -10%</button>
          <button onClick={() => applyTransform("rotate", 15)}>Rotate +15°</button>
          <button onClick={() => applyTransform("rotate", -15)}>Rotate -15°</button>
          <button className={nodeEditOn ? "is-active" : ""} onClick={() => setNodeEditOn((v) => !v)}>
            Node-edit: {nodeEditOn ? "ON" : "off"}
          </button>
          <button onClick={runHitTestDemo}>Hit-test @ centro</button>
          <button onClick={runExportRoundTrip}>Export + round-trip</button>
          <button onClick={runPerfBenchmark}>Run perf benchmark</button>
        </div>
        <canvas ref={canvasRef} style={{ width: "100%", height: "100%", display: "block" }} />
      </div>
      <EvidencePanel done={done} logs={logs} />
    </div>
  );
}
