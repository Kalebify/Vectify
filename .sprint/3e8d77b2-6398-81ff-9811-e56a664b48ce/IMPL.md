# IMPL — M2.1-S06 · Editor General — Workspace VECTORiZE

Estado: Implementado. Frontend-only (no se tocó `backend/` ni
`services/python-engine/`, salvo para verificarlos como parte del ciclo de
comandos pedido — ambos quedaron intactos, ver "Verificación").

## 1. Resumen de lo construido

El Workspace (`EditorShell`) es una pantalla nueva y completa, montada
DETRÁS de un toggle explícito en `App.tsx` ("Abrir en el Workspace", visible
una vez que hay una paleta confirmada) — el flujo clásico de `App.tsx`
(paleta → capas → preprocess → threshold → vectorize → simplify → check →
dimension → export) sigue existiendo intacto, porque sigue siendo el único
lugar donde se detecta/confirma la paleta y se generan las capas
(precondiciones del propio Workspace). Ninguna tarjeta pidió todavía retirar
ese flujo.

```
EditorShell
├── EditorHeader        (VECTORiZE · ← Projects · nombre · dirty-state honesto · Undo/Redo/Save/Export placeholders)
├── EditorToolbar        (11 íconos del wireframe, solo Select/Pan reales)
├── VectorCanvas          (Konva/react-konva — zoom/pan reales, SVG real por capa)
├── aside (right rail)
│    ├── PreviewNavigator (miniatura + rectángulo de viewport)
│    ├── EditorLayersPanel (adaptación del panel derecho "LAYERS")
│    └── InspectorPanel   (envuelve LayerInfoPanel + ManufacturingOperationSummary)
└── footer
     ├── EditorStatusBar  (zoom −/%/+, FIT, dimensiones px, GRID/SNAP placeholders)
     └── PaletteBar        (swatches de la paleta confirmada)
```

## 2. `useVectorDocument` — estado de dominio

`frontend/src/hooks/useVectorDocument.ts`. Agregación 100% del lado del
CLIENTE, tal como recomendó la resolución de ambigüedades del spec — no hay
ninguna entidad `VectorDocument` nueva en el backend.

Fuentes (todas de solo lectura, ninguna dispara un cálculo nuevo):
1. `GET .../color-palette/{paletteId}` — paleta confirmada. Agregué
   `getColorPalette` a `api/colorPaletteApi.ts` (el endpoint YA existía en
   `ColorPaletteEndpoints.cs`, `GetColorPalette`; el cliente frontend
   simplemente no lo usaba todavía).
2. `GET .../color-palette/{paletteId}/layers` — conjunto de capas, SOLO SI
   YA EXISTE (nunca genera). Agregué `getVectorLayers` a
   `api/vectorLayersApi.ts` (mismo criterio: el endpoint GET ya existía en
   `VectorLayerEndpoints.cs`, `GetVectorLayers`, devuelve 404 si nadie pidió
   "Generar capas" todavía — el `POST` que sí genera es el que usa
   `useVectorLayers`/`LayersPanel` clásico, deliberadamente NO reusado acá).
3. `GET .../color-palette/{paletteId}/layers/consolidated` — info
   consolidada (M2.1-S03/M2.1-S04), ya existía y ya se usaba
   (`useConsolidatedVectorLayers`); acá se llama directo desde el hook
   nuevo en vez de reusar ese hook, porque `useConsolidatedVectorLayers`
   depende de un `layerSetId` que en este flujo llega recién después del
   paso 2, dentro de la misma cadena async — encadenarlo como un hook
   aparte hubiera significado 2 renders extra + lógica de sincronización
   redundante con la que ya hace `useVectorDocument` internamente.

Máquina de estados: `idle → loading → (ready | empty | error)`. `empty` es
un estado explícito con 4 razones (`no_palette_selected`,
`palette_not_found`, `palette_not_confirmed`, `layers_not_generated`) — el
DoD del spec ("no hay datos falsos") se cumple literalmente: un proyecto sin
capas generadas NUNCA cae en `ready` con un array vacío disfrazado de
"documento", cae en `empty` con copy explicando qué falta.

`visibility`/`toggleVisibility`/`isolate`/`showAll`/`selectedGroupId`/
`selectGroup` viven en el mismo hook (no en un hook aparte ni en cada
panel): son el mismo patrón que `useVectorLayers.isolate/showAll` de
M2.1-S04, reimplementado acá (no importado) porque `useVectorLayers` trae
consigo su propio `generate()`/fetch que no aplica a este flujo de
solo-lectura — pero el ALGORITMO es idéntico a propósito, para no introducir
una segunda semántica de "aislar una capa" en la misma app.

## 3. `VectorCanvas` — nivel de interactividad de Konva

`frontend/src/components/editor/VectorCanvas.tsx` +
`frontend/src/lib/svgTransform.ts`.

**Qué es real en esta tarjeta:**
- Zoom con la rueda del mouse, centrado en el puntero.
- Pan por arrastre (con la herramienta Pan activa, o manteniendo Espacio
  como shortcut temporal sobre Select — documentado en el `aria-label` del
  canvas).
- Fit-to-screen real (botón FIT de `EditorStatusBar`).
- Reusa `useCanvasTransform` (M1-S06) TAL CUAL — mismo hook que ya usa
  `components/vectorize/VectorCanvas.tsx` para el visualizador de un solo
  SVG. No se reescribió ni un ápice de esa matemática: el "recurso" que se
  centra/escala pasó de ser un `<img>` a ser un `<Layer>` de Konva con
  `offsetX/offsetY` = mitad del documento, pero la fórmula de zoom-al-
  puntero y el álgebra de pan son EXACTAMENTE las mismas.
- Carga el SVG REAL de cada capa (`fetch(layer.svgUrl)`), lo parsea con
  `lib/svgTransform.ts` (patrón documentado en el ADR de M2.1-S05 —
  "Konva no tiene importador nativo, se reusa un parser de texto
  compartido" — escrito de cero para producción, NO es el código del
  spike) y dibuja un `<Path>` de Konva por cada `<path>` real del SVG, con
  su `fill` y su `transform` (`translate`/`scale`/`rotate`/`matrix`,
  descompuestos a `x/y/rotation/scaleX/scaleY/skewX` porque Konva no acepta
  una matriz SVG cruda). Verificado contra SVG reales del pipeline
  (`backend/.../vector-layers/**/*.svg`, que usan `transform="translate(x,y)"`)
  — 17 tests en `svgTransform.test.ts`.
- Selección BÁSICA: click sobre un `<Path>` (herramienta Select) llama a
  `onSelectGroup(layer.groupId)`; la capa seleccionada se resalta con un
  `stroke` (no solo el color de fill — accesibilidad: "nunca depender solo
  del color").

**Qué NO es real (deliberado, fuera de alcance de esta tarjeta):**
- Sin multi-select, sin `<Transformer>` (mover/escalar/rotar), sin edición
  de nodos Bézier, sin rubber-band. El spec es explícito: "selección/
  transform pueden ser básicos... esas son herramientas de MVP3". El
  spike de M2.1-S05 ya demostró que un `<Transformer>` funcional es
  ~10 líneas adicionales (`KonvaSpike.tsx`) — se dejó fuera a propósito
  para no adelantar herramientas de edición reales, que es precisamente lo
  que "Fuera de alcance" prohíbe.
- Deseleccionar clickeando el fondo vacío: el `onMouseDown` del `Stage`
  detecta el click en fondo pero no hace nada (comentario explícito en el
  código) — `selectedGroupId` vive en `useVectorDocument`, que no expone
  un "deselect"; agregar uno hubiera sido una decisión de producto no
  pedida por el spec, así que se dejó documentado como no-op en vez de
  inventar el comportamiento.

## 4. Decisiones de Save/Export (header)

- **Save**: placeholder deshabilitado. La persistencia real del
  `VectorDocument` es M2.1-S08 (tarjeta siguiente, nombre explícito
  "Persistencia del VectorDocument y reapertura") — implementar Save acá
  hubiera sido invadir esa tarjeta.
- **Dirty/saved**: el wireframe muestra un check ("✓") fijo. Se decidió
  NO copiarlo literalmente: sin persistencia real, un check permanente
  sería el dato falso que el propio DoD prohíbe ("no hay datos falsos").
  En su lugar, `EditorHeader` muestra "Sin guardado automático todavía".
- **Export**: placeholder deshabilitado. `ExportPanel` (M1-S10) exporta un
  `VectorVersion`/`SimplificationVersion`/`DimensionVersion` del pipeline
  de UN SOLO vector de MVP1 (`readyVector`/`readySimplification`/
  `readyDimension` en `App.tsx`) — esos objetos no existen en el contexto
  del Workspace (que trabaja sobre `paletteId` + capas por color, un
  concepto distinto). No hay hoy un endpoint que exporte el
  `VectorDocument` multicapa completo como un solo archivo, y crear uno no
  fue pedido por esta tarjeta ("no inventes que algo funciona"). Reusar
  `ExportPanel` tal cual habría exportado una sola capa arbitraria fuera de
  contexto, lo cual es peor que un placeholder honesto.
- **Undo/Redo**: placeholders deshabilitados, sin historial de edición (no
  hay operaciones de edición reales todavía que deshacer).

## 5. `EditorLayersPanel` — por qué no se reusó `LayersPanel` completo

El `LayersPanel` existente (`components/layers/LayersPanel.tsx`) es el
panel MONOLÍTICO de M2-S02..M2.1-S04: orquesta `useVectorLayers` +
`useLayerComponents` + `useComponentGroups` + `usePhysicalUnion` +
`useManufacturingOperations` + `useConsolidatedVectorLayers` +
`useExplodedView` + `useComparisonMode`, y renderiza SU PROPIO canvas
(`LayerCanvas.tsx`, basado en `<img>`) y SU PROPIO `LayerInfoPanel`
interno. Montarlo tal cual dentro del Workspace habría duplicado tanto el
Canvas (el suyo, `<img>`, contra `VectorCanvas` nuevo de Konva) como el
Inspector (su `LayerInfoPanel` interno, contra `InspectorPanel`) — dos
"fuentes de verdad" visuales del mismo documento en la misma pantalla,
exactamente lo que el spec pide evitar ("el estado de dominio no debe
quedar atrapado dentro de componentes visuales" / "ningún panel debe
guardar su propia copia divergente").

En su lugar, `EditorLayersPanel.tsx` es un componente NUEVO, data-driven
por `VectorDocument` (la única fuente de verdad de esta tarjeta), que
reproduce el patrón visual de `LayerList` (swatch + nombre + visibilidad +
badge de operación) adaptado al wireframe del panel derecho ("👁 🔵 Blue
🔒"). El panel monolítico anterior sigue existiendo intacto y se sigue
usando en el flujo clásico de `App.tsx` — no se tocó ni una línea de su
lógica de negocio (component groups / physical union / exploded view /
comparison mode siguen siendo responsabilidad exclusiva de esa rama de la
app, fuera de alcance de esta tarjeta).

`InspectorPanel` sí envuelve/reusa `LayerInfoPanel` TAL CUAL (sin tocar su
código), adaptando `VectorDocumentLayer` a los 2 tipos que espera
(`VectorLayerPayload`/`ConsolidatedVectorLayerPayload`) — coincide
exactamente con "Color / Paths / Pieces / Operation" del wireframe. También
reusa `ManufacturingOperationSummary` (M2-S07) tal cual, con un resumen
calculado client-side a partir de `document.layers` (evita una llamada de
red extra: la info ya está en el `VectorDocument` agregado).

## 6. Otras decisiones no triviales

- **PreviewNavigator**: en vez de un segundo parser de paths Konva, reusa
  el patrón de `LayerCanvas.tsx` (M2-S02) — apilar `<img>` por capa
  visible — porque la miniatura no necesita interactividad real, y mantiene
  el mismo criterio de "defensa en profundidad" (nunca SVG inline +
  `dangerouslySetInnerHTML`) que el resto del visualizador. El rectángulo
  de viewport se calcula invirtiendo la misma transform que aplica
  `VectorCanvas` (mismo `useCanvasTransform`, mismo `containerSize`).
- **Dimensiones en `EditorStatusBar`**: se muestran en PX (tamaño interno
  real del SVG, siempre disponible), no en mm — mostrar "210 × 297 mm" sin
  que el proyecto tenga una `DimensionVersion` aplicada (M1-S09) habría
  sido un dato inventado. No se integró `DimensionPanel`/`dimensionScale.ts`
  en el Workspace en esta tarjeta (el spec lo ofrece como opcional: "si ya
  hay una versión con dimensión aplicada" — el Workspace hoy no conoce
  ninguna `DimensionVersion` porque no navega desde ese estado de
  `App.tsx`); queda documentado como extensión futura, no como omisión
  silenciosa.
- **`EditorLayersPanel__add` / `PaletteBar__add`**: "+ ADD LAYER" y "+" del
  wireframe están presentes pero deshabilitados — crear una capa/color desde
  cero (no derivada de detección) es una herramienta de edición real, fuera
  de alcance explícito de esta tarjeta.
- **Full-bleed layout**: `#root` (definido en `App.css`) limita
  `max-width: 720px` y centra — apropiado para el pipeline clásico, pero
  incompatible con "el Canvas debe dominar la pantalla" del spec.
  `editor.css` saca a `.editor-shell` de esa restricción con un truco CSS
  estándar (`position:relative; left:50%; width:100vw;
  transform:translateX(-50%)`) sin tocar el CSS global que las demás
  pantallas siguen necesitando.
- **Resize de paneles**: "paneles redimensionables si es razonable" se
  resolvió con `resize: horizontal` nativo del navegador sobre
  `.editor-shell__right-rail`, en vez de escribir un sistema de arrastre
  propio para algo que el spec mismo no exige al 100%.
- **`canvas` (node-canvas) como devDependency**: Konva necesita un
  `HTMLCanvasElement.getContext('2d')` real para montar un `Stage` — jsdom
  no lo implementa por su cuenta. Sin este paquete, TODOS los tests de
  `VectorCanvas`/`EditorShell` habrían sido imposibles de escribir
  (confirmado empíricamente: un `<Stage>` mínimo tira `TypeError` al
  montar sin él). Es una dependencia de TESTING pura (no se importa desde
  ningún código de producción ni viaja al bundle de `vite build`).
- **`vitest.config.ts`**: se subió `testTimeout` (5s → 30s) y se agregó
  `maxWorkers: 8`. Montar un `Stage` real de Konva sobre `canvas` nativo es
  bastante más caro en CPU que el resto de los componentes del repo; bajo
  la suite COMPLETA en paralelo (34 archivos) en una máquina con carga
  externa, el timeout por defecto empezó a fallar de forma intermitente
  incluso en tests sin nada async — confirmado como contención de
  recursos, no un bug de lógica (los mismos tests son 100% estables en
  aislamiento, y la suite completa corrió en verde repetidamente tras el
  ajuste). No se redujo el paralelismo a 1 (ralentizaría toda la suite
  existente de forma innecesaria); se topeó a 8 como punto medio.

## 7. Ambigüedades resueltas (siguiendo las recomendaciones del spec)

Las 3 ambigüedades del spec se resolvieron exactamente como recomendaba el
orquestador: `VectorDocument` es agregación cliente (sección 2), Save
placeholder + Export placeholder con la justificación de la sección 4,
interactividad mínima de Konva = zoom/pan/fit reales + selección básica
(sección 3).

## 8. Cobertura de los criterios de aceptación ampliados

| Criterio | Cobertura |
|---|---|
| Componentes separados según arquitectura pedida | `EditorShell`, `EditorToolbar`, `VectorCanvas`, `EditorLayersPanel`, `PaletteBar`, `InspectorPanel`, `PreviewNavigator`, `EditorStatusBar` — 8 archivos, cada uno con su test |
| Estado de dominio fuera de componentes visuales | `useVectorDocument` — único, consumido por todos los paneles |
| VectorCanvas Konva con zoom/pan reales | Sección 3 |
| Toolbar shell completo, mayoría deshabilitada | `EditorToolbar` — 11 íconos, 2 habilitados, tooltips "MVP3" |
| Header con dirty/Undo/Redo/Save/Export honestos | `EditorHeader`, sección 4 |
| Bottom bar zoom/palette/dimensiones/Grid/Snap | `EditorStatusBar` + `PaletteBar` |
| Integración real, sin datos falsos | `useVectorDocument` nunca sintetiza capas; estado `empty` explícito |
| Accesibilidad | `aria-label`/`aria-pressed`/`title` en todos los botones interactivos; foco visible heredado de `:focus-visible` global (botones reales, nunca `div onClick`); shortcuts documentados en el `aria-label` del canvas (Espacio, +/-, flechas); selección nunca depende solo de color (stroke en canvas, badge ✓ en Layers/Palette, borde en la fila) |
| Responsive desktop-first | `editor.css`, 3 breakpoints (1100px/900px/720px), se degrada sin romperse |
| Tests: render completo / sin layers / multicolor / resize / zoom-fit / error / sincronización | `EditorShell.test.tsx` cubre los 7 explícitamente |
| Fuera de alcance respetado | Ningún Crop/Draw/Erase/Boolean/Nodes/Bridges/IA implementado, ni parcialmente — todos como botones deshabilitados con tooltip |

## 9. Nota sobre el estado del repo al terminar

Se detectaron 2 commits automáticos ya presentes en la rama al momento de
escribir este documento (`bcd3f75`, `44acbb3`, mensajes `wip(3e8d77b2):
M2.1-S06 ...`) que esta sesión NO creó explícitamente (nunca se invocó
`git commit`) — parecen provenir de un mecanismo de snapshot/checkpoint
externo a este agente. Se documentan acá por transparencia; no se intentó
deshacerlos ni modificarlos (`git reset`/`git commit --amend` están fuera
de lo permitido).
