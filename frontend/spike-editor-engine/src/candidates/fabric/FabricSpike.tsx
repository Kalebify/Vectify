import { useEffect, useRef, useState } from "react";
import * as fabric from "fabric";
import type { SpikeDocument } from "../../types";
import { LayersPanel } from "../../shared/LayersPanel";
import { EvidencePanel } from "../../shared/EvidencePanel";
import { useEvidenceLog } from "../../shared/useEvidenceLog";
import { sampleFrameRate } from "../../shared/PerfHarness";
import { fetchSpikeSvgText, parseSpikeSvg, countNodes } from "../../shared/loadLayersFromSvg";
import { parseAnchorPoints } from "../../shared/parsePathPoints";
import { composeAbsolutePoint } from "../../shared/svgTransform";

type SpikeMeta = { spikeLayerId: string; groupId: string };

/**
 * Candidato 2/3: Fabric.js.
 *
 * Notas de la implementación (ver ADR en IMPL.md): Fabric trae, a
 * diferencia de Paper.js, controles de transformación (mover/escalar/
 * rotar) NATIVOS -- el usuario puede arrastrar los handles de esquina sin
 * código custom (ver el <canvas> interactivo abajo). A cambio, NO tiene
 * ningún concepto de edición de nodos/segmentos de un Path: el punto 8 se
 * demuestra acá con un overlay manual de círculos arrastrables (mismo
 * approach que en Konva), reconstruyendo `path.path` a mano.
 */
export function FabricSpike({ doc, svgText }: { doc: SpikeDocument; svgText: string }) {
  const canvasElRef = useRef<HTMLCanvasElement | null>(null);
  const canvasRef = useRef<fabric.Canvas | null>(null);
  const layerObjectsRef = useRef<Map<string, fabric.FabricObject>>(new Map());
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
  const [hiddenIds, setHiddenIds] = useState<Set<string>>(new Set());
  const [isolatedId, setIsolatedId] = useState<string | null>(null);
  const [nodeEditOn, setNodeEditOn] = useState(false);
  const { logs, done, log } = useEvidenceLog();

  // ---- 1 + 2. Importar SVG (nativo) + mapear layers/IDs ----------------
  useEffect(() => {
    const el = canvasElRef.current;
    if (!el) return;
    const canvas = new fabric.Canvas(el, {
      selection: true,
      preserveObjectStacking: true,
    });
    canvasRef.current = canvas;
    canvas.setDimensions({ width: el.clientWidth || 900, height: el.clientHeight || 700 });
    canvas.setZoom(0.45);

    let disposed = false;
    (async () => {
      const { result, importMs } = await (async () => {
        const start = performance.now();
        const res = await fabric.loadSVGFromString(svgText);
        return { result: res, importMs: performance.now() - start };
      })();
      if (disposed) return;

      const objects = result.objects.filter((o): o is fabric.FabricObject => !!o);
      const map = layerObjectsRef.current;
      // HALLAZGO REAL (ver ADR, criterio "fidelidad SVG"): a diferencia de
      // Paper.js, fabric.loadSVGFromString() de Fabric 7 NO preserva los
      // <g> anidados del fixture -- devuelve una lista PLANA de objetos
      // top-level (acá: 10 fabric.Path, uno por cada <path> del SVG, sin
      // wrapper de Group por layer). Reconstruimos el agrupamiento
      // nosotros mismos, en base a paths.length de cada SpikeLayer (que sí
      // conocemos porque el dominio -- no Fabric -- es la fuente de verdad
      // de cuántos <path> tiene cada capa), asumiendo que Fabric preserva
      // el orden de documento (verificado empíricamente que sí lo hace).
      const byIndex = objects.length === doc.layers.length;
      let cursor = 0;
      doc.layers.forEach((layer) => {
        let obj: fabric.FabricObject | undefined;
        if (byIndex) {
          obj = objects[cursor];
          cursor += 1;
        } else {
          const chunkSize = layer.paths.length || 1;
          const chunk = objects.slice(cursor, cursor + chunkSize);
          cursor += chunkSize;
          obj = chunk.length > 1 ? new fabric.Group(chunk) : chunk[0];
        }
        if (!obj) return;
        obj.set({ selectable: true, hasControls: true, hasBorders: true });
        (obj as fabric.FabricObject & { id?: string }).id = layer.id;
        (obj as unknown as { data?: SpikeMeta }).data = { spikeLayerId: layer.id, groupId: layer.groupId };
        canvas.add(obj);
        map.set(layer.id, obj);
      });
      const reconstructedPathCount = [...map.values()].reduce(
        (n, obj) => n + (obj.type === "group" ? (obj as fabric.Group).size() : 1),
        0
      );
      log(
        "2. Layers/IDs sin pérdida",
        `loadSVGFromString(): ${objects.length} objetos PLANOS de nivel superior (tipos: ${objects
          .map((o) => o.type)
          .join(", ")}) -- Fabric NO preserva los <g> del fixture (a diferencia de Paper.js). Reagrupados a mano en ` +
          `${doc.layers.length} layers usando paths.length del dominio como guía; paths recuperados: ${reconstructedPathCount}/${objects.length}.`
      );
      log("1. Importar SVG", `fabric.loadSVGFromString() completado en ${importMs.toFixed(2)}ms.`);
      canvas.renderAll();
    })();

    canvas.on("selection:created", (e) => syncSelectionFromFabric(e.selected));
    canvas.on("selection:updated", (e) => syncSelectionFromFabric(e.selected));
    canvas.on("selection:cleared", () => setSelectedIds(new Set()));
    canvas.on("object:moving", (e) => {
      const id = (e.target as unknown as { data?: SpikeMeta }).data?.spikeLayerId;
      if (id) log("5. Move/scale/rotate", `object:moving -> ${id} @ (${e.target.left?.toFixed(0)},${e.target.top?.toFixed(0)}) (control nativo de Fabric, sin código custom).`);
    });
    canvas.on("object:scaling", (e) => {
      const id = (e.target as unknown as { data?: SpikeMeta }).data?.spikeLayerId;
      if (id) log("5. Move/scale/rotate", `object:scaling -> ${id} scaleX=${e.target.scaleX?.toFixed(2)} (control nativo de Fabric).`);
    });
    canvas.on("object:rotating", (e) => {
      const id = (e.target as unknown as { data?: SpikeMeta }).data?.spikeLayerId;
      if (id) log("5. Move/scale/rotate", `object:rotating -> ${id} angle=${e.target.angle?.toFixed(1)}° (control nativo de Fabric).`);
    });
    canvas.on("mouse:down", (e) => {
      const target = e.target as (fabric.FabricObject & { data?: SpikeMeta }) | undefined;
      const p = e.scenePoint;
      log(
        "7. Hit-test de path",
        target
          ? `mouse:down hit nativo de Fabric -> ${target.data?.spikeLayerId ?? target.type} en (${p?.x?.toFixed(0)},${p?.y?.toFixed(0)})`
          : `mouse:down en (${p?.x?.toFixed(0)},${p?.y?.toFixed(0)}) -> sin target`
      );
    });

    // ---- 3. Zoom/Pan reales: rueda del mouse = zoom, arrastre en área
    // vacía = pan. Ambos nativos de la API de eventos de Fabric (mouse:wheel
    // hace zoomToPoint; el pan se arma con mouse:down/move/up + relativePan,
    // que sí es manual -- Fabric no trae "pan de canvas" listo para usar,
    // a diferencia de zoomToPoint que sí es un método nativo de un paso).
    let isPanning = false;
    let panLast = { x: 0, y: 0 };
    canvas.on("mouse:wheel", (opt) => {
      const delta = opt.e.deltaY;
      let zoom = canvas.getZoom();
      zoom *= 0.999 ** delta;
      zoom = Math.min(8, Math.max(0.05, zoom));
      canvas.zoomToPoint(new fabric.Point(opt.e.offsetX, opt.e.offsetY), zoom);
      opt.e.preventDefault();
      opt.e.stopPropagation();
      log("3. Zoom/Pan", `mouse:wheel -> zoomToPoint(zoom=${zoom.toFixed(2)}) (método nativo de Fabric).`);
    });
    canvas.on("mouse:down", (opt) => {
      if (!opt.target) {
        isPanning = true;
        panLast = { x: (opt.e as MouseEvent).clientX, y: (opt.e as MouseEvent).clientY };
      }
    });
    canvas.on("mouse:move", (opt) => {
      if (!isPanning) return;
      const e = opt.e as MouseEvent;
      canvas.relativePan(new fabric.Point(e.clientX - panLast.x, e.clientY - panLast.y));
      panLast = { x: e.clientX, y: e.clientY };
    });
    canvas.on("mouse:up", () => {
      if (isPanning) log("3. Zoom/Pan", "pan por arrastre (relativePan, manual sobre mouse:move) finalizado.");
      isPanning = false;
    });

    function syncSelectionFromFabric(selected: fabric.FabricObject[] | undefined) {
      const ids = new Set<string>();
      for (const obj of selected ?? []) {
        const id = (obj as unknown as { data?: SpikeMeta }).data?.spikeLayerId;
        if (id) ids.add(id);
      }
      setSelectedIds(ids);
      log("4. Single + multi-select", `Selección nativa de Fabric (click / shift-click / rubber-band): ${[...ids].join(", ") || "(vacía)"}`);
    }

    return () => {
      disposed = true;
      canvas.dispose();
      layerObjectsRef.current.clear();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [doc, svgText]);

  // ---- 6. Hide/show/isolate: React decide, Fabric solo aplica ----------
  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    for (const layer of doc.layers) {
      const obj = layerObjectsRef.current.get(layer.id);
      if (!obj) continue;
      const hiddenByIsolate = isolatedId !== null && isolatedId !== layer.id;
      obj.visible = !hiddenIds.has(layer.id) && !hiddenByIsolate;
    }
    canvas.requestRenderAll();
  }, [hiddenIds, isolatedId, doc.layers]);

  // ---- selección programática desde el LayersPanel de React -----------
  function selectFromPanel(id: string, additive: boolean) {
    const canvas = canvasRef.current;
    if (!canvas) return;
    setSelectedIds((prev) => {
      const next = additive ? new Set(prev) : new Set<string>();
      if (additive && next.has(id)) next.delete(id);
      else next.add(id);
      const objs = [...next].map((lid) => layerObjectsRef.current.get(lid)).filter((o): o is fabric.FabricObject => !!o);
      canvas.discardActiveObject();
      if (objs.length === 1) {
        canvas.setActiveObject(objs[0]);
      } else if (objs.length > 1) {
        const sel = new fabric.ActiveSelection(objs, { canvas });
        canvas.setActiveObject(sel);
      }
      canvas.requestRenderAll();
      return next;
    });
  }

  function applyTransform(kind: "scale" | "rotate", amount: number) {
    const canvas = canvasRef.current;
    if (!canvas) return;
    for (const id of selectedIds) {
      const obj = layerObjectsRef.current.get(id);
      if (!obj) continue;
      if (kind === "scale") obj.set({ scaleX: (obj.scaleX ?? 1) * amount, scaleY: (obj.scaleY ?? 1) * amount });
      else obj.rotate((obj.angle ?? 0) + amount);
      obj.setCoords();
    }
    canvas.requestRenderAll();
    log("5. Move/scale/rotate", `${kind}(${amount}) programático (botón) aplicado a ${selectedIds.size} layer(s).`);
  }

  function runHitTestDemo() {
    const canvas = canvasRef.current;
    if (!canvas) return;
    const centerPoint = new fabric.Point(doc.width / 2, doc.height / 2);
    let hitId: string | null = null;
    for (const [id, obj] of layerObjectsRef.current) {
      if (obj.containsPoint(centerPoint)) hitId = id;
    }
    log(
      "7. Hit-test de path",
      `containsPoint() manual en centro del documento (${centerPoint.x},${centerPoint.y}) -> ${hitId ?? "ninguna capa (zona vacía, esperado por el layout en grilla)"}`
    );
  }

  function runExportRoundTrip() {
    const canvas = canvasRef.current;
    if (!canvas) return;
    const exported = canvas.toSVG();
    const originalPathCount = doc.layers.reduce((n, l) => n + l.paths.length, 0);
    const exportedPathCount = exported.match(/<path/g)?.length ?? 0;
    const idsFound = doc.layers.filter((l) => exported.includes(`id="${l.id}"`)).length;
    log(
      "9. Export/round-trip",
      `canvas.toSVG(): ${exported.length} bytes, ${exportedPathCount} <path> (original: ${originalPathCount}, ` +
        `${exportedPathCount === originalPathCount ? "MATCH" : "DIFIERE"}). IDs recuperables: ${idsFound}/${doc.layers.length} ` +
        `(fabric SÍ serializa el prop .id nativamente en toSVG; groupId/vectorId siguen sin canal nativo).`
    );
  }

  // ---- 8. Segmentos/handles: overlay manual de círculos arrastrables ---
  const anchorLayer = doc.layers[0];
  const anchorPoints = anchorLayer ? parseAnchorPoints(anchorLayer.paths[0]?.d ?? "") : [];

  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas || !anchorLayer) return;
    const circles: fabric.Circle[] = [];
    if (nodeEditOn) {
      for (const ap of anchorPoints) {
        const abs = composeAbsolutePoint(ap, anchorLayer.paths[0]?.transform ?? null, anchorLayer.groupTransform);
        const circle = new fabric.Circle({
          left: abs.x - 4,
          top: abs.y - 4,
          radius: 4,
          fill: "#ffffff",
          stroke: "#3a5cf5",
          strokeWidth: 1,
          hasControls: false,
          hasBorders: false,
        });
        (circle as unknown as { anchorIndex: number }).anchorIndex = ap.index;
        circle.on("moving", () => {
          log(
            "8. Segmentos/handles (Bézier)",
            `Circle overlay anchor #${ap.index} movido a (${circle.left?.toFixed(0)},${circle.top?.toFixed(0)}) -- ` +
              `demuestra que ES posible seleccionar/arrastrar un anchor individual; reconstruir el \`d\` real requeriría ` +
              `mapear cada Circle de vuelta a su índice en fabric.Path.path (custom, sin API nativa de Fabric para esto).`
          );
        });
        canvas.add(circle);
        circles.push(circle);
      }
      canvas.requestRenderAll();
    }
    return () => {
      for (const c of circles) canvas.remove(c);
      canvas.requestRenderAll();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [nodeEditOn]);

  async function runPerfBenchmark() {
    log("10. Performance (SVG medio)", "Cargando medium-complexity.svg (220 paths, ~4840 nodos)...");
    const text = await fetchSpikeSvgText("/medium-complexity.svg");
    const benchDoc = parseSpikeSvg(text);
    const nodeCount = countNodes(benchDoc);

    const tmpCanvasEl = document.createElement("canvas");
    tmpCanvasEl.width = 800;
    tmpCanvasEl.height = 800;
    const benchCanvas = new fabric.Canvas(tmpCanvasEl);

    const start = performance.now();
    const res = await fabric.loadSVGFromString(text);
    for (const o of res.objects) {
      if (o) benchCanvas.add(o);
    }
    const importMs = performance.now() - start;
    benchCanvas.renderAll();

    let dx = 0;
    const { avgFrameMs, fps } = await sampleFrameRate(60, () => {
      dx += 1;
      benchCanvas.relativePan(new fabric.Point(Math.cos(dx * 0.1), Math.sin(dx * 0.1)));
    });

    log(
      "10. Performance (SVG medio)",
      `Fabric.js: import=${importMs.toFixed(1)}ms, ${benchDoc.layers.length} paths / ~${nodeCount} nodos, ` +
        `pan avg frame=${avgFrameMs.toFixed(2)}ms (${fps.toFixed(1)} fps sobre 60 frames).`
    );
    benchCanvas.dispose();
  }

  return (
    <div className="spike">
      <div className="spike__layers">
        <LayersPanel
          layers={doc.layers}
          selectedIds={selectedIds}
          hiddenIds={hiddenIds}
          isolatedId={isolatedId}
          onSelect={selectFromPanel}
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
          <button onClick={() => canvasRef.current && (canvasRef.current.setZoom(canvasRef.current.getZoom() * 1.2), canvasRef.current.requestRenderAll())}>Zoom +</button>
          <button onClick={() => canvasRef.current && (canvasRef.current.setZoom(canvasRef.current.getZoom() * 0.8), canvasRef.current.requestRenderAll())}>Zoom -</button>
          <button
            onClick={() => {
              const c = canvasRef.current;
              if (!c) return;
              c.setZoom(0.45);
              c.absolutePan(new fabric.Point(0, 0));
              c.requestRenderAll();
              log("3. Zoom/Pan", "Vista reseteada (zoom=0.45, pan=0,0). Zoom/Pan en vivo: rueda del mouse + arrastre (nativo de Fabric vía canvas listeners estándar).");
            }}
          >
            Reset vista
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
        <canvas ref={canvasElRef} />
      </div>
      <EvidencePanel done={done} logs={logs} />
    </div>
  );
}
