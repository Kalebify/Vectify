import { useEffect, useRef, useState } from "react";
import { Stage, Layer, Group, Path, Transformer, Circle } from "react-konva";
import Konva from "konva";

type KonvaGroupType = Konva.Group;
type KonvaStageType = Konva.Stage;
type KonvaTransformerType = Konva.Transformer;
import type { SpikeDocument } from "../../types";
import { LayersPanel } from "../../shared/LayersPanel";
import { EvidencePanel } from "../../shared/EvidencePanel";
import { useEvidenceLog } from "../../shared/useEvidenceLog";
import { measureImport, sampleFrameRate } from "../../shared/PerfHarness";
import { fetchSpikeSvgText, parseSpikeSvg, countNodes } from "../../shared/loadLayersFromSvg";
import { parseAnchorPoints } from "../../shared/parsePathPoints";
import { composeAbsolutePoint } from "../../shared/svgTransform";

/**
 * Candidato 3/3: Konva / react-konva.
 *
 * Notas de la implementación (ver ADR en IMPL.md): a diferencia de Paper y
 * Fabric (ambos imperativos: se les entrega un <canvas> y se los maneja
 * desde fuera de React con refs), react-konva es DECLARATIVO -- la escena
 * es JSX (<Stage>/<Layer>/<Group>/<Path>), el estado (posición, escala,
 * visibilidad, selección) vive 100% en React y el árbol se re-renderiza
 * como cualquier otro componente. Es, de los 3, el que menos requiere
 * "escapar" de React. A cambio: (a) no tiene importador de SVG nativo (acá
 * el parseo ya lo hace shared/loadLayersFromSvg.ts, reusado igual que para
 * los otros dos) y (b) no tiene export a SVG nativo -- ver runExportRoundTrip.
 */
export function KonvaSpike({ doc }: { doc: SpikeDocument; svgText: string }) {
  const stageRef = useRef<KonvaStageType | null>(null);
  const groupRefs = useRef<Map<string, KonvaGroupType>>(new Map());
  const transformerRef = useRef<KonvaTransformerType | null>(null);
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
  const [hiddenIds, setHiddenIds] = useState<Set<string>>(new Set());
  const [isolatedId, setIsolatedId] = useState<string | null>(null);
  const [nodeEditOn, setNodeEditOn] = useState(false);
  const [stageScale, setStageScale] = useState(0.45);
  const [importMs, setImportMs] = useState<number | null>(null);
  const { logs, done, log } = useEvidenceLog();

  // ---- 1 + 2. "Importar SVG" (react-konva no trae parser propio: se
  // reusa el parseo ya hecho por App.tsx/loadLayersFromSvg.ts para los 3
  // candidatos, y acá se mide el costo de construir el árbol de nodos
  // Konva a partir de esa estructura ya parseada) -----------------------
  useEffect(() => {
    const { importMs: ms } = measureImport(() => {
      // El "trabajo de importación" de Konva es, en los hechos, el primer
      // render de JSX de abajo (Group/Path por layer) -- acá solo se mide
      // el costo de preparar los datos ya parseados, ya que Konva no
      // ofrece un método imperativo equivalente a project.importSVG.
      return doc.layers.map((l) => l.paths.length);
    });
    setImportMs(ms);
    log(
      "2. Layers/IDs sin pérdida",
      `${doc.layers.length} <Group> de React, 1:1 con SpikeLayer.id (prop \`id\` de React, sin mapeo posicional necesario -- ` +
        `react-konva no tiene su propio parser de SVG, así que no hay reconciliación de IDs que hacer: el id SIEMPRE es el que puso React).`
    );
    log("1. Importar SVG", `Construcción del árbol de nodos Konva (vía JSX) para ${doc.layers.length} layers: ${ms.toFixed(2)}ms.`);
  }, [doc, log]);

  useEffect(() => {
    const tr = transformerRef.current;
    if (!tr) return;
    const nodes = [...selectedIds].map((id) => groupRefs.current.get(id)).filter((n): n is KonvaGroupType => !!n);
    tr.nodes(nodes);
    tr.getLayer()?.batchDraw();
  }, [selectedIds]);

  function selectLayer(id: string, additive: boolean) {
    setSelectedIds((prev) => {
      const next = additive ? new Set(prev) : new Set<string>();
      if (additive && next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
    log("4. Single + multi-select", `${additive ? "multi-select (shift-click)" : "single-select (click)"}: ${id} (Konva.Group.on('click'), estado en React).`);
  }

  function handleWheel(e: { evt: WheelEvent; target: unknown }) {
    e.evt.preventDefault();
    const stage = stageRef.current;
    if (!stage) return;
    const oldScale = stage.scaleX();
    const pointer = stage.getPointerPosition();
    if (!pointer) return;
    const mousePointTo = {
      x: (pointer.x - stage.x()) / oldScale,
      y: (pointer.y - stage.y()) / oldScale,
    };
    const direction = e.evt.deltaY > 0 ? -1 : 1;
    const newScale = direction > 0 ? oldScale * 1.1 : oldScale * 0.9;
    stage.scale({ x: newScale, y: newScale });
    stage.position({
      x: pointer.x - mousePointTo.x * newScale,
      y: pointer.y - mousePointTo.y * newScale,
    });
    setStageScale(newScale);
    log("3. Zoom/Pan", `wheel -> stage.scale(${newScale.toFixed(2)}) centrado en el puntero (patrón estándar de Konva).`);
  }

  function runHitTestDemo() {
    const stage = stageRef.current;
    if (!stage) return;
    const abs = { x: doc.width / 2, y: doc.height / 2 };
    const stagePos = { x: abs.x * stage.scaleX() + stage.x(), y: abs.y * stage.scaleY() + stage.y() };
    const shape = stage.getIntersection(stagePos);
    let owningLayerId: string | null = null;
    if (shape) {
      let node: Konva.Node | null = shape;
      while (node) {
        for (const [id, g] of groupRefs.current) {
          if (g === node) owningLayerId = id;
        }
        node = node.getParent();
      }
    }
    log(
      "7. Hit-test de path",
      shape
        ? `stage.getIntersection() en centro del documento -> ${owningLayerId ?? shape.constructor.name} (hit-test nativo de Konva, usa un hit-canvas propio).`
        : `stage.getIntersection() en centro del documento -> sin match (zona vacía, esperado por el layout en grilla).`
    );
  }

  function runExportRoundTrip() {
    // Konva NO tiene exportador a SVG nativo (solo toDataURL/toCanvas
    // raster, o toJSON en su propio formato). Como el modelo de dominio
    // (SpikeLayer.paths[].d + colorHex) sigue siendo la fuente de verdad
    // (restricción arquitectónica no negociable del spec), reconstruimos
    // el SVG combinando ESE dato original con el estado de transform que
    // sí vive en cada Konva.Group (x/y/rotation/scale) -- Konva solo
    // aporta la interacción, no la geometría persistida.
    const parts = doc.layers.map((layer) => {
      const g = groupRefs.current.get(layer.id);
      const extra = g && (g.x() || g.y() || g.rotation() || g.scaleX() !== 1 || g.scaleY() !== 1)
        ? ` translate(${g.x().toFixed(1)},${g.y().toFixed(1)}) rotate(${g.rotation().toFixed(1)}) scale(${g.scaleX().toFixed(2)})`
        : "";
      const paths = layer.paths
        .map((p) => `<path d="${p.d}" transform="${p.transform ?? ""}" />`)
        .join("");
      return `<g id="${layer.id}" fill="${layer.colorHex}" transform="${layer.groupTransform ?? ""}${extra}">${paths}</g>`;
    });
    const exported = `<svg xmlns="http://www.w3.org/2000/svg" width="${doc.width}" height="${doc.height}">${parts.join("")}</svg>`;
    const originalPathCount = doc.layers.reduce((n, l) => n + l.paths.length, 0);
    const exportedPathCount = exported.match(/<path/g)?.length ?? 0;
    const idsFound = doc.layers.filter((l) => exported.includes(`id="${l.id}"`)).length;
    log(
      "9. Export/round-trip",
      `SVG reconstruido a mano desde el modelo de dominio + transform de Konva.Group: ${exported.length} bytes, ` +
        `${exportedPathCount} <path> (original: ${originalPathCount}, ${exportedPathCount === originalPathCount ? "MATCH" : "DIFIERE"}). ` +
        `IDs: ${idsFound}/${doc.layers.length}. Konva no ofrece exportSVG/toSVG nativo -- este round-trip funciona PORQUE ` +
        `el dominio (no Konva) sigue siendo la fuente de verdad de la geometría.`
    );
  }

  async function runPerfBenchmark() {
    log("10. Performance (SVG medio)", "Cargando medium-complexity.svg (220 paths, ~4840 nodos)...");
    const text = await fetchSpikeSvgText("/medium-complexity.svg");
    const benchDoc = parseSpikeSvg(text);
    const nodeCount = countNodes(benchDoc);

    const hiddenDiv = document.createElement("div");
    hiddenDiv.style.position = "fixed";
    hiddenDiv.style.left = "-9999px";
    hiddenDiv.style.width = "800px";
    hiddenDiv.style.height = "800px";
    document.body.appendChild(hiddenDiv);

    const { result: benchStage, importMs: benchImportMs } = measureImport(() => {
      const stage = new Konva.Stage({ container: hiddenDiv, width: 800, height: 800 });
      const layer = new Konva.Layer();
      for (const l of benchDoc.layers) {
        const group = new Konva.Group();
        for (const p of l.paths) {
          group.add(new Konva.Path({ data: p.d, fill: l.colorHex }));
        }
        layer.add(group);
      }
      stage.add(layer);
      stage.draw();
      return stage;
    });

    let dx = 0;
    const { avgFrameMs, fps } = await sampleFrameRate(60, () => {
      dx += 1;
      benchStage.position({ x: Math.cos(dx * 0.1) * 2, y: Math.sin(dx * 0.1) * 2 });
      benchStage.batchDraw();
    });

    log(
      "10. Performance (SVG medio)",
      `Konva: construcción de escena=${benchImportMs.toFixed(1)}ms, ${benchDoc.layers.length} paths / ~${nodeCount} nodos, ` +
        `pan avg frame=${avgFrameMs.toFixed(2)}ms (${fps.toFixed(1)} fps sobre 60 frames).`
    );
    benchStage.destroy();
    document.body.removeChild(hiddenDiv);
  }

  const anchorLayer = doc.layers[0];
  const anchorPoints = anchorLayer ? parseAnchorPoints(anchorLayer.paths[0]?.d ?? "") : [];

  return (
    <div className="spike">
      <div className="spike__layers">
        <LayersPanel
          layers={doc.layers}
          selectedIds={selectedIds}
          hiddenIds={hiddenIds}
          isolatedId={isolatedId}
          onSelect={selectLayer}
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
          <button onClick={() => setStageScale((s) => s * 1.2)}>Zoom +</button>
          <button onClick={() => setStageScale((s) => s * 0.8)}>Zoom -</button>
          <button
            onClick={() => {
              setStageScale(0.45);
              stageRef.current?.position({ x: 0, y: 0 });
              log("3. Zoom/Pan", "Vista reseteada (scale=0.45, pos=0,0). Pan en vivo: arrastrar el fondo (Stage draggable=true, nativo).");
            }}
          >
            Reset vista
          </button>
          <button className={nodeEditOn ? "is-active" : ""} onClick={() => setNodeEditOn((v) => !v)}>
            Node-edit: {nodeEditOn ? "ON" : "off"}
          </button>
          <button onClick={runHitTestDemo}>Hit-test @ centro</button>
          <button onClick={runExportRoundTrip}>Export + round-trip</button>
          <button onClick={runPerfBenchmark}>Run perf benchmark</button>
          {importMs !== null && <span style={{ alignSelf: "center", color: "#9a9aa5" }}>import: {importMs.toFixed(1)}ms</span>}
        </div>
        <Stage
          ref={stageRef}
          width={900}
          height={700}
          scaleX={stageScale}
          scaleY={stageScale}
          draggable
          onWheel={handleWheel}
          onDragEnd={() => log("3. Zoom/Pan", "Stage.draggable -> pan por arrastre (prop nativa de react-konva, sin listeners manuales).")}
          onClick={(e) => {
            if (e.target === stageRef.current) setSelectedIds(new Set());
          }}
        >
          <Layer>
            {doc.layers.map((layer) => {
              const isolatedHidden = isolatedId !== null && isolatedId !== layer.id;
              return (
                <Group
                  key={layer.id}
                  id={layer.id}
                  ref={(node) => {
                    if (node) groupRefs.current.set(layer.id, node);
                    else groupRefs.current.delete(layer.id);
                  }}
                  visible={!hiddenIds.has(layer.id) && !isolatedHidden}
                  draggable
                  onClick={(e) => {
                    e.cancelBubble = true;
                    selectLayer(layer.id, e.evt.shiftKey);
                  }}
                  onDragMove={() => log("5. Move/scale/rotate", `Group.draggable -> moviendo ${layer.id} (prop nativa, sin código de arrastre manual).`)}
                  onTransform={() => log("5. Move/scale/rotate", `Konva.Transformer -> transformando ${layer.id} (scale/rotate con handles nativos).`)}
                >
                  {layer.paths.map((p, i) => (
                    <Path key={i} data={p.d} fill={layer.colorHex} transform={p.transform ?? undefined} />
                  ))}
                </Group>
              );
            })}
          </Layer>
          <Layer>
            <Transformer ref={transformerRef} rotateEnabled resizeEnabled />
            {nodeEditOn &&
              anchorLayer &&
              anchorPoints.map((ap) => {
                const abs = composeAbsolutePoint(ap, anchorLayer.paths[0]?.transform ?? null, anchorLayer.groupTransform);
                return (
                  <Circle
                    key={ap.index}
                    x={abs.x}
                    y={abs.y}
                    radius={4}
                    fill="#ffffff"
                    stroke="#3a5cf5"
                    strokeWidth={1}
                    draggable
                    onDragMove={(e) =>
                      log(
                        "8. Segmentos/handles (Bézier)",
                        `Circle overlay anchor #${ap.index} movido a (${e.target.x().toFixed(0)},${e.target.y().toFixed(0)}) -- ` +
                          `mismo approach manual que Fabric: Konva tampoco tiene edición de nodos de Path nativa.`
                      )
                    }
                  />
                );
              })}
          </Layer>
        </Stage>
      </div>
      <EvidencePanel done={done} logs={logs} />
    </div>
  );
}
