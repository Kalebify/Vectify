# M2.1-S06 · Editor General — Workspace VECTORiZE
URL: https://app.notion.com/p/3e8d77b2639881ff9811e56a664b48ce

Sexta tarjeta de MVP 2.1. Construye la pantalla central del editor (el "Workspace"), integrando en un solo layout todo lo que MVP2/MVP2.1 ya construyó como paneles sueltos (paleta, layers, componentes, operación de fabricación) más un Canvas interactivo NUEVO basado en Konva/react-konva (decisión de M2.1-S05). Depende explícitamente de esa tarjeta — "S06 puede comenzar sin volver a debatir la librería" era su Definition of Done.

Stack: Frontend + Vector + ASP.NET Core (backend solo si hace falta algo puntual de lectura, ver Ambigüedades).

## Requerimiento (transcripción del cuerpo de la tarjeta)

### Objetivo (propiedad Notion)
Construir la pantalla central del editor integrando Canvas, Toolbar, Layers, Palette, Preview, Inspector y controles de documento con identidad propia orientada a fabricación láser.

### Criterio de aceptación (propiedad Notion)
Un proyecto multicolor abre en un Workspace funcional; se visualizan canvas, layers y paleta sincronizados, preview, inspector, zoom/fit y shell de herramientas sin implementar anticipadamente las herramientas avanzadas de MVP 3.

### Visión
Esta es la pantalla central de VECTORiZE. Toma como referencia conceptual un editor vectorial por capas, pero debe diseñarse para fabricación láser y para nuestras entidades Layer/Path/Component/ManufacturingOperation.

### Wireframe obligatorio
```
┌───────────────────────────────────────────────────────────────────────────────┐
│ VECTORiZE   ← Projects   Project.svg ✓             ↶  ↷      SAVE     EXPORT │
├──────────┬─────────────────────────────────────────────┬──────────────────────┤
│ TOOLS    │                                             │ PREVIEW              │
│ ↖ Select │                                             │ [document overview]  │
│ ✋ Pan   │                                             ├──────────────────────┤
│ ✂ Crop   │                 CANVAS                      │ LAYERS               │
│ ↔ Move   │                                             │ 👁 🔵 Blue       🔒 │
│ ▣ Fill   │                                             │ 👁 🟡 Yellow     🔒 │
│ ◉ Color  │                                             │ 👁 🔴 Red        🔒 │
│ ✎ Draw   │                                             │ 👁 ⚫ Black      🔒 │
│ ⌫ Erase  │                                             │ + ADD LAYER          │
│ ⤢ Offset │                                             ├──────────────────────┤
│ ✂ Cut    │                                             │ INSPECTOR            │
│ ⌁ Path   │                                             │ Color / Paths /      │
│          │                                             │ Pieces / Operation   │
├──────────┴─────────────────────────────────────────────┴──────────────────────┤
│ −  68%  + │ FIT │ 🔵 🟡 🔴 ⚫ [+] │ 210 × 297 mm │ GRID │ SNAP              │
└───────────────────────────────────────────────────────────────────────────────┘
```

### Layout
Header: proyecto, dirty/saved state, Undo/Redo placeholders, Save, Export.
Left Toolbar: shell de herramientas; en este sprint solo Pan/Select si ya son necesarios para navegar.
Center: Canvas responsive con zoom, pan y fit.
Right: Preview + Layers + Inspector.
Bottom: zoom, palette, dimensiones mm y placeholders Grid/Snap.

### Diseño
No copiar visualmente SVGTrace. Usar sistema consistente de spacing, iconografía, tooltips, estados active/disabled, paneles redimensionables si es razonable y responsive desktop-first. El Canvas debe dominar la pantalla.

### Arquitectura frontend
Separar EditorShell, EditorToolbar, VectorCanvas, LayersPanel, PaletteBar, InspectorPanel, PreviewNavigator y EditorStatusBar. El estado de dominio no debe quedar atrapado dentro de componentes visuales.

### Integración
Abrir un proyecto existente y cargar su VectorDocument real. Nada de layers hardcodeados para la demo final.

### Accesibilidad
Botones con nombre accesible, shortcuts documentados, foco visible y tooltips. No depender solo del color para selección/estado.

### Tests
Render del workspace, proyecto sin layers, proyecto multicolor, resize, zoom/fit, error de carga y sincronización básica con documento.

### Definition of Done
El usuario reconoce un editor completo aunque varias herramientas estén deshabilitadas hasta MVP 3. No hay datos falsos y el layout sirve como base estable para todos los sprints posteriores.

### Fuera de alcance
Implementar Crop, Draw, Erase, Boolean, Nodes, Bridges o IA en esta tarjeta.

## Estado actual (reutilizar, no reescribir)

Ya existen y funcionan (todos de MVP2/MVP2.1, a integrar dentro del nuevo layout, no reescribir su lógica): paleta de colores (`ColorPalettePanel`), capas vectoriales (`useVectorLayers`, visibility/isolate/showAll de M2.1-S04), info consolidada por capa (`useConsolidatedVectorLayers`, `LayerInfoPanel` — nombre/hex/paths/componentes/operación), modo comparación Original/Compuesto/Aislado (`useComparisonMode`), dimensiones físicas en mm (`DimensionPanel`/`dimensionScale.ts`, M1-S09), operación de fabricación (`ManufacturingOperationSummary`). El SPIKE de M2.1-S05 (`frontend/spike-editor-engine/`) documenta cómo integrar Konva/react-konva — reutilizar ese aprendizaje (patrones de `KonvaSpike.tsx`), no el código del spike en sí (vive fuera de `src/`, es descartable).

## Criterios de aceptación ampliados

- [ ] **Componentes separados según la arquitectura pedida**: `EditorShell` (layout raíz), `EditorToolbar` (columna de herramientas izquierda), `VectorCanvas` (Konva, centro), `LayersPanel` (ya existe, adaptar al panel derecho), `PaletteBar` (barra inferior de swatches — nuevo, distinto del `ColorPalettePanel` de detección/edición de paleta), `InspectorPanel` (nuevo, muestra color/paths/piezas/operación de lo seleccionado — puede envolver/reusar `LayerInfoPanel`), `PreviewNavigator` (nuevo, miniatura del documento completo), `EditorStatusBar` (barra inferior: zoom, dimensiones mm, Grid/Snap placeholders).
- [ ] **Estado de dominio fuera de los componentes visuales**: un hook/contexto propio (ej. `useVectorDocument` o similar) que agregue paleta confirmada + capas + componentes + operaciones en un solo objeto de "documento" consumido por TODOS los paneles — ningún panel visual debe guardar su propia copia divergente del estado de dominio. Ver Ambigüedades sobre si esto requiere un endpoint backend nuevo o es agregación del lado del cliente sobre los endpoints ya existentes.
- [ ] **VectorCanvas con Konva/react-konva**: reemplaza el `<img>` estático usado hasta ahora en `LayerCanvas.tsx` (M2-S02) por un canvas Konva interactivo, con zoom/pan reales (rueda del mouse + fit-to-screen) — al menos zoom/pan funcionales en esta tarjeta; selección/transform/etc. pueden ser más básicos (esta tarjeta NO implementa las herramientas de edición de MVP3, solo el shell del canvas). Cargar el SVG real de cada capa (mismo enfoque que el spike: parsear `d`/`transform` de cada `<path>`, no reinventar el parser — reusar `svgTransform.ts`/lógica equivalente si aplica, o el parser ya usado en el pipeline de producción donde corresponda).
- [ ] **Toolbar con shell completo pero mayoría deshabilitada**: TODOS los íconos del wireframe presentes (Select, Pan, Crop, Move, Fill, Color, Draw, Erase, Offset, Cut, Path) — solo Select/Pan funcionales si hacen falta para navegar el canvas, el resto visiblemente DESHABILITADOS (no ocultos) con tooltip indicando que llegan en MVP3. Nunca implementar Crop/Draw/Erase/Boolean/Nodes/Bridges/IA de verdad.
- [ ] **Header**: nombre del proyecto, indicador dirty/saved (placeholder está bien si la persistencia real es de M2.1-S08 — no fingir un estado "Saved" falso, ver Accesibilidad/DoD "no hay datos falsos"), Undo/Redo (placeholders deshabilitados, sin funcionalidad todavía), Save, Export (Export puede reutilizar el `ExportPanel`/flujo ya existente de M1-S10 si aplica, o quedar como placeholder si no encaja todavía en este layout — documentar la decisión).
- [ ] **Bottom bar**: control de zoom (+/-/%), botón Fit, swatches de la paleta confirmada (clic para seleccionar color, mismo groupId compartido que el resto), dimensiones mm (reusar `dimensionScale.ts`/`DimensionPanel` si ya hay una versión con dimensión aplicada), Grid/Snap como placeholders visualmente presentes pero sin funcionalidad real todavía.
- [ ] **Integración real, sin datos falsos**: el Workspace debe cargar el `VectorDocument` real de un proyecto YA EXISTENTE (paleta confirmada + capas generadas) — nunca layers hardcodeados de demo. Si un proyecto no tiene paleta/capas generadas aún, mostrar un estado vacío/de carga honesto, no datos simulados.
- [ ] **Accesibilidad**: todo botón con nombre accesible (`aria-label`/texto visible), shortcuts de teclado documentados (aunque sean pocos en esta tarjeta — ej. Space para pan, +/- para zoom), foco visible (outline, no `outline: none` sin reemplazo), tooltips en los íconos de la toolbar, nunca depender solo del color para indicar selección/estado (agregar borde/ícono/texto adicional).
- [ ] **Responsive desktop-first**: el layout debe funcionar bien en anchos de escritorio típicos: paneles redimensionables si es razonable (no obligatorio al 100%, "si es razonable" lo dice el propio spec), pero debe degradarse sin romperse en anchos más chicos.
- [ ] **Tests**: render del workspace completo, proyecto sin layers (estado vacío honesto), proyecto multicolor (caso principal), resize, zoom/fit, error de carga (ej. paleta/capas no encontradas), y sincronización básica con el documento (cambiar de capa seleccionada se refleja en Inspector/Preview/Canvas).
- [ ] Fuera de alcance: NO implementar Crop, Draw, Erase, Boolean, Nodes, Bridges, IA — ni siquiera parcialmente, deben quedar como placeholders deshabilitados.

## Referencias visuales / de marca
El wireframe ASCII de arriba ES la referencia — no hay mockups gráficos adjuntos. "No copiar visualmente SVGTrace" (herramienta de referencia conceptual externa, no replicar su estética).

## Umbrales de calidad
No aplican Lighthouse/axe formales, pero dado el énfasis explícito en accesibilidad del propio spec, prestar atención real (nombres accesibles, foco visible, no depender solo de color). Estándar ya usado: `npm run build` (no solo `npm test`) sin errores, los test suites completos en verde.

## Ambigüedades detectadas
- **¿"VectorDocument" es un modelo backend nuevo o una agregación del lado del cliente?** No hay una entidad `VectorDocument` persistida hoy (M2.1-S08, la tarjeta SIGUIENTE, se llama explícitamente "Persistencia del VectorDocument y reapertura" — sugiriendo que la persistencia real de UN documento consolidado todavía no existe). Recomendación del orquestador: para ESTA tarjeta, `VectorDocument` es una agregación del lado del CLIENTE sobre los endpoints ya existentes (paleta confirmada + `GET .../layers/consolidated`), sin necesidad de un endpoint backend nuevo — así S06 no bloquea a S08, que es quien real y explícitamente construye la persistencia. Si el implementador encuentra que hace falta un endpoint de lectura nuevo puntual (ej. "traer todo de una sola vez"), que sea aditivo y de solo lectura, documentado.
- **Alcance de Save/Export en el header**: no especificado en detalle si deben ser funcionales en esta tarjeta. Recomendación: Save puede ser un placeholder deshabilitado (la persistencia real es M2.1-S08); Export puede reusar el flujo ya existente de M1-S10 si encaja naturalmente, o quedar como placeholder — documentar la decisión tomada.
- **Nivel de interactividad real del VectorCanvas**: el spec pide "zoom/pan" como criterio de aceptación explícito, pero no exige selección/transform completos en esta tarjeta (esas son herramientas de MVP3). El implementador decide cuánto de la interactividad demostrada en el spike de M2.1-S05 vale la pena traer ya a producción en esta tarjeta vs. dejar para cuando se implementen las herramientas reales — mínimo: zoom/pan funcionales, capas visibles con su color real, aislar/mostrar todo ya conectado al mismo estado de M2.1-S04.
- Ninguna otra ambigüedad bloqueante.

## Nota de orquestación
Sexta tarjeta de MVP 2.1, la más grande en superficie de UI hasta ahora. Corrida en modo autónomo (mismo criterio ya establecido). Recordatorio: correr `npm run build` además de `npm test`.
