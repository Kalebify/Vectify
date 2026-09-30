# M2.1-S07 · Layers + paleta interactiva del Editor
URL: https://app.notion.com/p/3e8d77b26398815e9743fc82ddd93ea1

Séptima tarjeta de MVP 2.1. Convierte el `EditorLayersPanel`/`PaletteBar` del Workspace (M2.1-S06, recién construido) de un listado mayormente pasivo a un navegador interactivo completo del documento — agregando lo que M2.1-S03 explícitamente dejó pendiente para "una tarjeta posterior": persistencia real de `order`/`visible`/`locked`, más Lock (concepto nuevo) y Drag & Drop para reordenar.

Stack: Frontend + Vector + Laser.

## Requerimiento (transcripción del cuerpo de la tarjeta)

### Objetivo (propiedad Notion)
Convertir Layers y Palette en controles interactivos sincronizados para navegar y entender un documento multicolor.

### Criterio de aceptación (propiedad Notion)
Seleccionar un swatch selecciona el Layer; hide/show, isolate, lock, rename y reorder funcionan y persisten sin alterar coordenadas de la geometría.

### Comportamiento
- Click swatch → seleccionar Layer correspondiente.
- Click Layer → seleccionar swatch correspondiente.
- Eye → hide/show.
- Isolate → mostrar solo selección sin borrar estados.
- Lock → impedir mutaciones del Layer.
- Rename → cambiar nombre, no id.
- Drag & drop → cambiar orden visual, no coordenadas.
- Select All in Layer → seleccionar sus paths.
- Mostrar pathCount y componentCount.

### Inspector de Layer
Color HEX, paths, componentes, operación CUT/ENGRAVE/IGNORE, warnings láser disponibles y acciones Isolate/Select All.

### Reglas
IDs estables. Color no debe usarse como identificador primario. Reorder no muta geometría. Un Layer bloqueado sigue siendo visible y seleccionable para inspección según UX definida, pero no editable.

### Persistencia
Guardar orden, nombre, visible, locked y operación. Decidir qué estado de selección es efímero.

### Tests
Sincronización palette↔layer, múltiples layers mismo color si el dominio lo permite, reorder, lock, reload y undo cuando corresponda.

### Definition of Done
El panel Layers deja de ser un listado pasivo y se convierte en el navegador principal del documento vectorial.

## Estado actual (reutilizar, no reescribir)

Ya existen y funcionan: sincronización swatch↔Layer (`selectedGroupId` compartido, M2.1-S04/M2.1-S06), Eye/hide-show + Isolate + Show All (`useVectorLayers`/`useVectorDocument`, M2.1-S04, HOY solo estado de sesión en memoria del cliente), `pathCount`/`componentCount`/`manufacturingOperation` (endpoint consolidado, M2.1-S03), Inspector con `LayerInfoPanel`+`ManufacturingOperationSummary` (M2.1-S06), Rename (endpoint `POST .../color-palette/{paletteId}/rename` ya existe desde M2-S01 — `VectorLayer.Name` viene directo de `ColorGroup.Name`, renombrar el grupo YA renombra la capa, reusar tal cual). Laser warnings YA EXISTEN como concepto (`CheckIssue`/Laser Checker, M1-S08) pero no están conectados al Inspector del Workspace todavía.

## Criterios de aceptación ampliados

- [ ] **Persistencia real de `order`/`visible`/`locked`** (lo más nuevo de esta tarjeta): hoy `visible` es SOLO estado de sesión en memoria (`useVectorLayers.visibility`/`useVectorDocument.visibility`), `order` es implícito (posición en la lista de la paleta), `locked` NO EXISTE. Backend: nueva entidad versionada persistida por capa (recomendación del orquestador, no vinculante: seguir el MISMO patrón ya usado por `ManufacturingOperationService`/`ManufacturingOperationSetVersion` — un sidecar propio por `groupId`+versión de capas, NO agregar estos campos a `ColorGroup`/`ColorPaletteVersion` que ya tienen su propia responsabilidad bien acotada). Persistir: `order` (entero), `visible` (booleano), `locked` (booleano). `name` y `operation` YA se persisten (rename existente de M2-S01; `ManufacturingOperationService` de M2-S07/MVP2) — esta tarjeta solo necesita EXPONERLOS ya combinados en el mismo lugar si no lo están, no reimplementar su persistencia.
- [ ] **Lock (concepto nuevo)**: un Layer bloqueado sigue siendo visible/seleccionable/inspeccionable (Eye, Isolate, Select All, ver su Inspector) pero NO editable — para esta tarjeta, "editable" se traduce en: no se puede empezar a mover/transformar su geometría en el Canvas (dado que MVP3 todavía no tiene edición de geometría real, el efecto concreto hoy es: el Canvas debe reflejar visualmente que ese Layer está bloqueado, ej. deshabilitar su `draggable`/selección de transform en Konva si ya hubiera alguna, y el ícono de Lock en el panel debe reflejar el estado). Togglear Lock NO debe afectar Eye/Isolate/visibilidad.
- [ ] **Drag & Drop para reordenar**: cambia el `order` persistido de las capas afectadas, NUNCA toca ningún `d`/`transform`/geometría — test explícito que lo confirme (mismo criterio que ya usaba M2.1-S03 para "reordenar no altera VectorId/SVG", ahora con persistencia real de por medio).
- [ ] **Select All in Layer**: acción que selecciona TODOS los paths/componentes de ese Layer en el Canvas (selección múltiple de la geometría de esa capa específicamente) — distinta del simple "seleccionar el Layer" (que ya existe). Dado que el Canvas de M2.1-S06 tiene selección básica por click (sin multi-select todavía), esta acción es el primer caso de selección múltiple real — implementarla de forma acotada a "todos los elementos de UN layer", no un multi-select general de la app.
- [ ] **Inspector ampliado**: agregar "warnings láser disponibles" (reusar el resultado ya existente del Laser Checker de M1-S08/`CheckIssue` si hay un check ya corrido para la capa seleccionada — si no hay un check corrido todavía para esa capa específica, mostrar un estado vacío honesto, NO correr un check nuevo automáticamente si eso no es trivial) y las acciones Isolate/Select All directamente en el panel (no solo en la lista de capas).
- [ ] **Múltiples layers del mismo color, "si el dominio lo permite"**: verificar si el dominio actual (una capa por `ColorGroup` único) puede producir 2 capas con el mismo `colorHex` — si NO es posible por construcción (colores únicos por definición de la paleta), documentarlo explícitamente y NO forzar un caso artificial; si SÍ es posible (ej. tras cierto merge/unmerge), agregar el test correspondiente.
- [ ] **"Color no debe usarse como identificador primario"**: auditoría rápida de que ninguna lógica de selección/comparación usa `colorHex`/`fill` como clave — siempre `groupId`. Si se encuentra algún lugar que sí lo hace (aunque sea incidental), corregirlo.
- [ ] **"Decidir qué estado de selección es efímero"**: el propio spec deja esto abierto — recomendación del orquestador: `selectedGroupId` (qué capa se está inspeccionando ahora mismo) es efímero (solo sesión, no se persiste); `order`/`visible`/`locked`/`name`/`operation` son persistentes. Documentar la decisión tomada.
- [ ] **Undo "cuando corresponda"**: dado que Undo/Redo quedaron como placeholders DESHABILITADOS en el header del Workspace (M2.1-S06, decisión ya tomada y documentada — la persistencia/historial reales son de MVP3), esta tarjeta NO necesita implementar una pila de Undo/Redo genérica. "Cuando corresponda" se interpreta como: si alguna acción de esta tarjeta es trivialmente reversible con la UI ya existente (ej. destildar Eye para deshacer un hide, volver a arrastrar para deshacer un reorder, Rename de vuelta), alcanza con que esa reversión manual sea posible — no hace falta un botón "Undo" dedicado. Documentar esta interpretación.
- [ ] **Tests**: sincronización palette↔layer (reverificar lo ya existente de M2.1-S04/S06), múltiples layers mismo color (o su ausencia documentada, ver arriba), reorder (persiste, no muta geometría), lock (bloquea edición, no bloquea visibilidad/inspección), reload (después de persistir order/visible/locked/name/operation, volver a cargar el documento — vía `useVectorDocument.reload()` o remontando — y confirmar que los valores persistidos sobreviven), y la interpretación de "undo" documentada arriba.

## Referencias visuales / de marca
MISSING — no hay links ni adjuntos en la tarjeta. El wireframe de M2.1-S06 sigue siendo la referencia visual base (esta tarjeta no lo cambia, lo hace interactivo).

## Umbrales de calidad
No aplican Lighthouse/axe formales. Estándar ya usado: `npm run build` (no solo `npm test`) sin errores, los test suites completos en verde.

## Ambigüedades detectadas
- **Dónde persistir `order`/`visible`/`locked`**: no especificado. Recomendación del orquestador: sidecar propio versionado por `groupId`, mismo patrón que `ManufacturingOperationService` — el implementador puede apartarse con justificación documentada.
- **Alcance exacto de "Select All in Layer"**: no cuantificado en detalle — el implementador decide cómo se ve una selección múltiple dentro de un solo Layer en el Canvas de Konva (ya que la selección multi-elemento no existía hasta esta tarjeta) y lo documenta.
- **Warnings láser en el Inspector**: no especifica si debe disparar un check nuevo o solo mostrar uno ya corrido — recomendación: mostrar el último resultado ya disponible, sin disparar un análisis nuevo automáticamente (evita llamadas costosas no solicitadas), documentar la decisión.
- Ninguna otra ambigüedad bloqueante.

## Nota de orquestación
Séptima tarjeta de MVP 2.1, corrida en modo autónomo (mismo criterio ya establecido). Recordatorio: correr `npm run build` además de `npm test`.
