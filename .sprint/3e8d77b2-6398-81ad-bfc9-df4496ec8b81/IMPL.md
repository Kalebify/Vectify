# ADR — Motor gráfico del Editor General (M2.1-S05)

Estado: Aceptado.
Decide: motor gráfico del futuro Editor General (M2.1-S06+).
No decide: cómo se implementa el editor completo (toolbar/inspector/estado — eso es M2.1-S06), ni la edición de nodos Bézier completa (MVP3).

## 1. Contexto

El Editor General (M2.1-S06 en adelante) necesita un motor gráfico de canvas para renderizar e interactuar con capas vectoriales (`VectorLayer`/`ColorGroup`, ya definidos en MVP2/MVP2.1). Antes de construirlo, esta tarjeta (spike, no feature) evalúa 3 candidatos con una prueba ejecutable real —el mismo SVG multicolor, los mismos 10 puntos obligatorios, los mismos 11 criterios— para no elegir por popularidad ni por lectura de documentación.

Restricción arquitectónica no negociable (repetida acá porque condiciona toda la evaluación): **React sigue controlando layout/toolbar/inspector/layers/estado de aplicación. El motor gráfico se limita a Canvas/geometría interactiva. El modelo de dominio (`VectorLayer`, `ColorGroup`) sigue siendo la fuente de verdad — el motor gráfico nunca la reemplaza.** Un candidato que empuje a violar esto es una desventaja real, no un detalle.

## 2. Candidatos evaluados

- **Paper.js** 0.12.18 (`paper`, incluye sus propios `.d.ts` — el paquete `@types/paper` es un stub obsoleto, confirmado por el warning de npm al instalarlo, y se removió de las devDependencies del spike).
- **Fabric.js** 7.4.0 (`fabric`, ESM-first, tipos propios).
- **Konva** 10.7.0 + **react-konva** 19.3.0 (react-konva versiona junto a React; requiere `react`/`react-dom` `^19.3.0` como peer).

No se agregó un 4º candidato: los 3 nombrados cubren el espacio de diseño relevante (imperativo con importador SVG nativo x2, declarativo-React x1) y ninguno mostró, durante el spike, una limitación que justificara sumar una cuarta librería.

## 3. Cómo se corrió el spike (evidencia ejecutable, no solo prosa)

### 3.1 Entrada compartida: el mismo SVG multicolor para los 3

`docker compose ps` falló al arrancar esta sesión (`cannot connect to the docker API`), así que no se pudo correr el flujo end-to-end en vivo (`POST /projects` → `.../color-palette/detect` → `.../confirm` → `.../layers`). En vez de inventar un SVG a mano desconectado del pipeline, `frontend/spike-editor-engine/fixtures/multicolor-input.svg` se compuso a partir de DOS fuentes reales ya producidas por corridas end-to-end previas y presentes en el repo (la procedencia exacta, con paths de archivo, queda documentada dentro del propio SVG en un comentario):

- **Geometría**: 4 `<g>` copiados literalmente (mismo `d`, mismo `transform`) de 4 archivos reales de salida de vectorización (`Vectorization/VectorVersion`) en `backend/Vectorify.Api/App_Data/uploads/**/vectors/*.svg`, de 4 proyectos subidos distintos durante QA real de M1/M2 (un anillo, una estrella, un bloque de 7 glifos de texto, y una mancha con ruido de trazado real de ~500 subpaths).
- **Color**: los 4 `fill` son los 4 hex reales detectados por `color-palette/detect` en la evidencia end-to-end de M2.1-S01 (`.sprint/3e8d77b2-6398-8166-bdd8-c969faca3d78/evidence/04_after_4colores_layer{1..4}_*.svg`): `#ff0000`, `#00c800`, `#ffdc00`, `#0000ff`.

Cada `<g>` lleva `data-group-id`/`data-name`/`data-color-hex`/`data-vector-id` espejando los campos reales de `VectorLayer` (`backend/Vectorify.Api/VectorLayers/VectorLayer.cs`: `GroupId`, `Name`, `ColorHex`, `VectorId`). Resultado: **4 layers, 10 `<path>` reales en total** (confirmado por el parser compartido, ver más abajo).

### 3.2 "Complejidad media" para el benchmark de rendimiento (punto 10)

Definición adoptada (documentada también en el script que lo genera): **220 paths × ~16-28 nodos c/u (mezcla de segmentos `L` y `C`) ≈ 4.950 nodos totales**, en un lienzo de 2000×2000 — deliberadamente ~10x más capas que el caso real más grande visto en QA del pipeline hasta M2.1-S04 (~20-30 grupos de color), para dejar margen antes de degradarse. Es sintético porque los fixtures reales disponibles localmente son o triviales (4 rectángulos) o un único `<path>` con ruido (no representativo de un documento multi-capa). Generado de forma determinista (seed fija) por `frontend/spike-editor-engine/fixtures/generate-medium-complexity.mjs` → `fixtures/medium-complexity.svg` (166 KB, 220 `<path>`, verificado por `grep -c '<path'`).

### 3.3 Método de medición

`frontend/spike-editor-engine/src/shared/PerfHarness.ts`: `importMs` = `performance.now()` antes/después de que cada candidato termine de construir su escena a partir de `medium-complexity.svg`; `avgFrameMs`/`fps` = promedio de 60 frames muestreados con `requestAnimationFrame` mientras se aplica un pan continuo sobre la escena ya importada (se descarta el primer frame). Mismo método, mismo fixture, misma máquina, para los 3 — no es un benchmark de laboratorio, es una medición real pero simple, documentada así explícitamente en el propio código.

### 3.4 Corrida real, en un navegador real, no simulada

Se instaló `puppeteer-core` (devDependency del spike, justificación: el spec exige números de rendimiento "reales, no estimaciones", y no hay forma de obtenerlos sin ejecutar la demo en un navegador de verdad) apuntando al Chrome ya instalado en la máquina (sin descargar un Chromium propio). `frontend/spike-editor-engine/scripts/run-headless-evidence.mjs` abre `npm run dev` (puerto 5183), entra a cada uno de los 3 tabs, hace click real en los botones de la toolbar (zoom, reset, hit-test, export+round-trip, perf benchmark) y en el canvas, y captura los `console.log()` reales que cada candidato emite. La corrida completa, con timestamps, queda en `.sprint/3e8d77b2-6398-81ad-bfc9-df4496ec8b81/evidence/headless-run-output.json`. Extracto textual (sin editar):

```
Paper.js:  import.SVG nativo: 4 grupos top-level detectados, mapeo por orden (índice) a los 4 layers del dominio -- 0 layers perdidos.
Paper.js:  exportSVG(): 9326 bytes, 10 <path> (original: 10, MATCH). IDs de layer recuperables: 4/4.
Paper.js:  Paper.js: import=42.5ms, 220 paths / ~4952 nodos, pan avg frame=16.32ms (61.3 fps sobre 60 frames).

Fabric.js: loadSVGFromString(): 10 objetos PLANOS de nivel superior (tipos: path x10) -- Fabric NO preserva los <g> del fixture (a diferencia de Paper.js). Reagrupados a mano en 4 layers usando paths.length del dominio como guía; paths recuperados: 10/10.
Fabric.js: canvas.toSVG(): 11782 bytes, 10 <path> (original: 10, MATCH). IDs recuperables: 4/4.
Fabric.js: import=19.0ms, 220 paths / ~4952 nodos, pan avg frame=16.35ms (61.2 fps sobre 60 frames).

Konva:     4 <Group> de React, 1:1 con SpikeLayer.id -- react-konva no tiene su propio parser de SVG, no hay reconciliación de IDs que hacer.
Konva:     SVG reconstruido a mano desde el modelo de dominio + transform de Konva.Group: 7849 bytes, 10 <path> (original: 10, MATCH). IDs: 4/4. Konva no ofrece exportSVG/toSVG nativo -- este round-trip funciona PORQUE el dominio (no Konva) sigue siendo la fuente de verdad.
Konva:     construcción de escena=15.7ms, 220 paths / ~4952 nodos, pan avg frame=16.33ms (61.2 fps sobre 60 frames).
```

**Hallazgo real no anticipado, documentado y corregido en el código**: en la primera corrida, `fabric.loadSVGFromString()` devolvió una lista PLANA de 10 objetos `Path` (Fabric no preservó los `<g>` anidados del fixture, a diferencia de Paper.js que sí preservó los 4 grupos exactos). Con el mapeo ingenuo (por índice/id) esto hacía perder 6 de los 10 paths reales (el layer de 7 glifos y el de la estrella con 2 paths quedaban con solo 1). Se corrigió reagrupando a mano los objetos planos usando `layer.paths.length` (que el dominio ya conoce) como guía — ~15 líneas de código adicional, ver `FabricSpike.tsx`. Esto es evidencia real de un costo de integración medible, no una opinión.

## 4. Los 10 puntos del spike obligatorio — qué funcionó, qué no, con qué esfuerzo

| # | Punto | Paper.js | Fabric.js | Konva/react-konva |
|---|---|---|---|---|
| 1 | Importar SVG | Nativo (`project.importSVG`), 42.5ms sobre el fixture de 220 paths | Nativo (`loadSVGFromString`), 19.0ms | Sin importador nativo — se reusa el parser compartido (`DOMParser`) + construcción de nodos Konva vía JSX, 15.7ms (no incluye parseo de string SVG, ver nota metodológica abajo) |
| 2 | Layers/IDs sin pérdida | Preserva los 4 `<g>` como `Group` con 0 código extra; mapeo por orden confirmado | Requirió reagrupar a mano (10 objetos planos → 4 grupos) — ver hallazgo arriba; una vez corregido, 0 pérdida | Trivial: el `id` de React ES el id del dominio, no hay reconciliación que hacer |
| 3 | Zoom/Pan | Manual (`view.zoom`/`view.center`, ~10 líneas en un `Tool`) | Semi-nativo (`zoomToPoint` nativo; pan por arrastre requiere `mouse:down/move/up` manual) | Semi-nativo (fórmula estándar de zoom-al-puntero sobre `Stage`; pan es `draggable` nativo del `Stage`) |
| 4 | Single + multi-select | Manual vía `project.hitTest` + estado en React (~20 líneas) | Nativo (`ActiveSelection`, shift-click, rubber-band — cero código de selección) | Manual pero simple (`onClick` + `e.evt.shiftKey`, estado 100% en React) |
| 5 | Move/scale/rotate | Sin handles nativos — mover es drag manual sobre el `Tool`; escalar/rotar son botones programáticos | Nativo: handles de esquina/rotación interactivos out-of-the-box, cero código de UI | Nativo vía `<Transformer>` (requiere wiring de refs, ~10 líneas, pero la interacción en sí es nativa) |
| 6 | Hide/show/isolate | `group.visible` seteado desde un `useEffect` que lee el estado de React — 0 lógica en el motor | Igual: `obj.visible` desde React | Igual, y más directo: `visible` es una prop de JSX del `<Group>` |
| 7 | Hit-test de path | Nativo y muy configurable (`project.hitTest` con `fill`/`stroke`/`segments`/`tolerance`) | Nativo para eventos de mouse; hit-test explícito por punto se implementó con `containsPoint()` (Fabric no expone un `findTarget` cómodo de usar fuera de un evento real) | Nativo (`stage.getIntersection`, hit-canvas propio por shape) |
| 8 | Segmentos/handles (Bézier futuro) | **Nativo**: `path.segments[i].point/handleIn/handleOut`; se implementó arrastre real de un segmento vía `project.hitTest({segments:true})` en el `Tool`, ~15 líneas | Sin soporte nativo: overlay manual de `fabric.Circle` posicionados con matemática de transform propia (`shared/svgTransform.ts`), ~30 líneas | Igual que Fabric: overlay manual de `<Circle>` de react-konva, mismo cálculo de transform compartido, ~25 líneas |
| 9 | Export/round-trip | Nativo (`project.exportSVG`), 10/10 paths, 4/4 IDs (requiere `item.name = id` explícito; metadata de dominio como `groupId` NO viaja) | Nativo (`canvas.toSVG`), 10/10 paths, 4/4 IDs (Fabric sí serializa `.id` nativamente) | **Sin exportador nativo** — se reconstruye el SVG a mano combinando el `d` original del dominio + el transform vivo de cada `Konva.Group`; funciona (10/10, 4/4) precisamente PORQUE el dominio ya es la fuente de verdad |
| 10 | Performance (SVG medio) | 61.3 fps, avg frame 16.32ms | 61.2 fps, avg frame 16.35ms | 61.2 fps, avg frame 16.33ms |

Nota metodológica sobre performance: los 3 resultados son estadísticamente indistinguibles (16.32–16.35ms, ~61fps) porque las 3 corridas están tapadas por el vsync de `requestAnimationFrame` (~60Hz) en esta máquina — con 220 paths / ~4.950 nodos **ninguno de los 3 candidatos mostró un cuello de botella medible**. La performance NO es un criterio decisivo entre estos 3 para este tamaño de documento; sí lo sería para documentos sustancialmente más grandes, que quedan fuera del alcance de este spike (ver Riesgos, sección 7).

## 5. Matriz de decisión (11 criterios explícitos, 1-5, 5 = mejor)

| Criterio | Paper.js | Fabric.js | Konva/react-konva |
|---|---|---|---|
| Fidelidad SVG (import/export nativo) | 5 — 4 grupos preservados exactos, round-trip 10/10 sin ajustes | 3 — nativo pero aplanó los `<g>` del fixture real (hallazgo empírico), requirió reagrupado manual | 2 — sin importador/exportador SVG nativo (mitigado por 3.4/arquitectura, ver abajo) |
| Facilidad de integración con React | 2 — 100% imperativo, requiere `paper.setup()`/refs/efectos manuales, scope global de Paper es una fuente de bugs sutiles si hay 2 instancias (se resolvió acá con `PaperScope` para el benchmark aislado) | 3 — imperativo pero con patrones bien documentados vía refs | 5 — `react-konva` es JSX declarativo; el estado vive 100% en React, sin capa de traducción |
| Path/node editing (Bézier futuro) | 5 — modelo `Segment` nativo, pensado para esto desde el diseño de la librería | 2 — sin modelo de nodos, todo manual | 2 — sin modelo de nodos, todo manual (mismo esfuerzo que Fabric) |
| Hit-testing | 5 — `hitTest` muy configurable (fill/stroke/segments/tolerance) | 4 — nativo en eventos de mouse; hit-test explícito por punto es más manual | 4 — `getIntersection` nativo y preciso (hit-canvas propio) |
| Layers (concepto de capa, hide/show/isolate, orden) | 4 — `Group`/`Layer` nativos | 3 — sin concepto de "Layer" propio más allá de z-order/`Group`, hay que construirlo | 5 — `Layer`/`Group` nativos, cada `Layer` de Konva es su propio `<canvas>` (ventaja real de aislamiento/perf para muchas capas) |
| Transforms (move/scale/rotate) | 2 — sin handles interactivos nativos, hay que construir la UI | 5 — handles de esquina/rotación nativos, cero código | 4 — `<Transformer>` nativo, requiere algo de wiring de refs |
| Serialization | 5 — `importSVG`/`exportSVG` nativos y simétricos | 4 — `loadSVGFromString`/`toSVG` nativos, con la salvedad de la sección 3.4 | 3 — solo `toJSON` en formato propio de Konva; SVG requiere reconstrucción manual (funciona, pero no es "gratis") |
| Performance | 4 — 61.3fps, import 42.5ms (el más lento de los 3 importando SVG nativo) | 4 — 61.2fps, import 19.0ms | 4 — 61.2fps, construcción de escena 15.7ms (nota: no incluye parseo de string SVG, ver sección 4) |
| Mantenibilidad | 3 — API estable y madura, pero el modelo imperativo + `PaperScope` global es una fuente de acoplamiento sutil a mantener a largo plazo | 4 — API estable, imperativa pero predecible, buena documentación | 5 — el 90% del código de interacción es JSX/hooks de React estándar, mismo patrón mental que el resto de la app (ver M2.1-S04, layers 100% React) |
| Soporte TypeScript | 4 — tipos propios reales (`dist/paper.d.ts`), el paquete `@types/paper` en npm está deprecado/es un stub | 5 — tipos propios completos, sin `any` necesario en el código del spike salvo para adjuntar metadata custom (igual que los otros 2) | 5 — tipos propios completos + tipado de componentes React de primera clase |
| Extensibilidad (a futuro: Bézier MVP3, más candidatos de interacción) | 4 — el modelo de `Segment` es la base ideal para editor de nodos completo | 3 — extensible pero cada feature nueva es más código imperativo a mano | 4 — extensible de forma idiomática a React (nuevos componentes/hooks), aunque la edición de nodos seguirá siendo custom |
| **Total (sobre 55)** | **43** | **40** | **43** |

Paper.js y Konva empatan en el puntaje bruto (43/55) pero por razones opuestas: Paper gana en fidelidad SVG nativa y en el único punto donde de verdad importa tener un motor "inteligente" (edición de nodos), Konva gana en todo lo que tiene que ver con vivir dentro de una app React de producción (integración, layers, mantenibilidad, extensibilidad idiomática) — que es, según la restricción arquitectónica de la sección 1, precisamente lo que más pesa para el próximo paso (M2.1-S06: construir el Workspace, con layout/toolbar/inspector/layers todos en React).

## 6. Recomendación final

**Konva + react-konva.**

Razón en una frase: es, de los 3, el único que no compite con React por el control del árbol de la aplicación — cero fricción con la restricción arquitectónica no negociable (React dueño de layout/toolbar/inspector/layers/estado; el motor gráfico solo dibuja y captura interacción), y su única debilidad real medida en este spike (sin importador/exportador SVG nativo) deja de ser un problema bajo esa misma restricción, porque el dominio (`VectorLayer`/`ColorGroup`) ya tiene que ser la fuente de verdad de la geometría de todas formas — Konva simplemente no ofrece la tentación de dejar que el motor "posea" el documento, que si es un riesgo real con Paper.js y Fabric.js (ambos con su propio `Project`/`Canvas` como modelo interno fuerte).

Por qué no los otros dos, explícitamente:
- **Paper.js** pierde por integración con React (2/5, el peor de los 3) y por no tener transforms interactivos nativos — dos cosas que S06 va a necesitar todos los días. Su ventaja real (edición de nodos nativa) es genuina y la más fuerte de los 3 candidatos, pero es una necesidad de MVP3 (Bézier), no de S06; y aun con Paper, la restricción arquitectónica obliga a sincronizar cualquier cambio de `Segment` de vuelta al dominio, así que el ahorro de código de Paper en ese punto es real pero menor de lo que parece a primera vista.
- **Fabric.js** es un buen segundo lugar (mejores transforms nativos que Konva, en igualdad con Paper en fidelidad SVG salvo por el hallazgo de la sección 3.4) pero no gana en ningún criterio de forma decisiva frente a Konva, y su modelo imperativo tiene el mismo costo de integración con React que Paper sin la ventaja de Paper en edición de nodos.

**Consecuencia explícita para M2.1-S06 y para MVP3**: la edición de segmentos/nodos Bézier (MVP3) va a requerir código custom con Konva (mismo esfuerzo demostrado acá que con Fabric, ~25 líneas para un prototipo de anchor points arrastrables) — esto es un costo conocido y aceptado, no una sorpresa a descubrir en MVP3. Si en algún punto la edición de nodos se vuelve el cuello de botella de desarrollo dominante, vale la pena revisar Paper.js específicamente para ESE subsistema (no para todo el editor) — pero eso es una decisión a tomar en MVP3 con evidencia de esa etapa, no ahora.

## 7. Riesgos conocidos / lo que este spike NO cubrió

- El benchmark de performance (220 paths/~4.950 nodos) no mostró diferencias porque ninguno de los 3 llegó a su límite en esta máquina — no hay evidencia de qué candidato degrada mejor con documentos mucho más grandes (miles de paths). Si el editor real necesita soportar documentos de esa escala, hace falta un benchmark de seguimiento antes de comprometerse más profundamente.
- No se probó edición de nodos con curvas `C`/`Q` reales (el overlay de anchor points del punto 8 usa un path con solo comandos `M`/`L` por simplicidad) — la viabilidad para curvas Bézier completas (no solo polígonos) queda pendiente de validar en MVP3.
- No se probó carga de SVG con features avanzadas de SVG (clipPath, gradientes, filtros) — el pipeline de Vectorify no las genera hoy, así que no era parte del spike obligatorio, pero si eso cambia hay que re-evaluar fidelidad de import en los 3.

## 8. La demo — qué queda y cómo correrla/verla

Todo el código del spike vive en `frontend/spike-editor-engine/`, fuera de `frontend/src/`, con su propio `package.json`/`node_modules`/`vite.config.ts` — no se importa desde ni se referencia en el build de producción de `frontend/`.

```
cd frontend/spike-editor-engine
npm install
npm run dev
# abrir http://localhost:5183 -- 3 tabs, uno por candidato
```

Cada tab carga `fixtures/multicolor-input.svg` (mismo SVG para los 3) y expone una toolbar que ejercita los 10 puntos del spike (zoom/pan, selección simple/múltiple con click/shift-click, mover con drag, escalar/rotar con botones, hide/show/isolate desde el panel de layers de la izquierda, hit-test explícito, edición de nodos con el botón "Node-edit", export+round-trip, y el benchmark de performance contra `fixtures/medium-complexity.svg`). El panel derecho de cada tab muestra en vivo qué puntos ya se ejercitaron en la sesión y un log de eventos (espejado en la consola del navegador).

Verificación de tipos/build del spike (aislada, no forma parte del build de producción):
```
npm run typecheck   # tsc --noEmit -> limpio
npm run build        # tsc --noEmit && vite build -> limpio (ver sección 9)
```

Corrida real automatizada (navegador real, no simulación) usada para capturar los números de la sección 3.4/4:
```
npm run dev &        # o en otra terminal
node scripts/run-headless-evidence.mjs
```
Salida completa (timestamps reales) archivada en `.sprint/3e8d77b2-6398-81ad-bfc9-df4496ec8b81/evidence/headless-run-output.json`.

## 9. Verificación de que el código de producción sigue intacto

El spike no toca `frontend/src/`, `backend/`, ni `services/python-engine/`. Baseline (antes de tocar nada) y verificación final (después de terminar el spike) dieron EXACTAMENTE los mismos números:

| Comando | Resultado |
|---|---|
| `dotnet build` (backend) | Compilación correcta — 0 advertencias, 0 errores |
| `dotnet test` (backend) | 631 superadas, 0 con error, 0 omitidas |
| `pytest` (services/python-engine, venv local) | 400 passed, 1 warning (deprecación de `httpx` en `starlette.testclient`, preexistente, no introducida por este spike) |
| `npm test` (frontend) | 23 test files passed, 191 tests passed |
| `npm run build` (frontend) | `tsc -b && vite build` — build limpio, mismo output que antes |

Build/typecheck del spike en sí (`frontend/spike-editor-engine`, fuera del alcance de "producción" pero verificado igual): `npm run typecheck` limpio, `npm run build` limpio (`tsc --noEmit && vite build`, bundle único de 1.09MB — hay un warning informativo de Vite sobre chunk size, esperable para un spike de 3 librerías gráficas cargadas en un solo bundle sin code-splitting; no aplica optimización de producción porque no lo es).
