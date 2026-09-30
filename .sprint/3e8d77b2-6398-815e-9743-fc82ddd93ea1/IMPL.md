# IMPL.md — M2.1-S07 · Layers + paleta interactiva del Editor

## Resumen

Convierte `EditorLayersPanel`/`PaletteBar`/`InspectorPanel`/`VectorCanvas` del
Workspace (M2.1-S06) en un navegador interactivo completo del documento
vectorial: persistencia real de `order`/`visible`/`locked` (backend), Lock
(concepto nuevo), Drag & Drop para reordenar, Select All in Layer (primera
selección múltiple real de la app), e Inspector ampliado con warnings láser
disponibles.

## Decisión central: dónde y cómo se persiste `order`/`visible`/`locked`

Nueva entidad versionada `LayerLayoutSetVersion` (`backend/Vectorify.Api/LayerLayout/`),
sidecar propio indexado por `(ProjectId, ImageId, PaletteId, PaletteVersion)` —
**exactamente el mismo patrón** que `ManufacturingOperationSetVersion`
(M2-S07/MVP2), el precedente más cercano:

- `LayerLayoutEntry(GroupId, Order, Visible, Locked)`: metadata pura por capa.
- `LayerLayoutSetVersion(..., Version, IReadOnlyList<LayerLayoutEntry> Entries, CreatedAt)`:
  versión INMUTABLE del conjunto completo — cada mutación (visibilidad, lock,
  reorder) crea una versión NUEVA, nunca muta una existente.
- `ILayerLayoutVersionRegistry` / `InMemoryLayerLayoutVersionRegistry` /
  `PersistentLayerLayoutVersionRegistry`: mismo contrato e implementación
  (sidecar JSON en disco, escritura atómica temp+move, rehidratación al
  arrancar tomando el máximo `Version` visto) que
  `PersistentManufacturingOperationVersionRegistry`. Ruta default:
  `App_Data/layer-layout/{ProjectId}/{ImageId}/{PaletteId}/{PaletteVersion}.json`.
- `ILayerLayoutService`/`LayerLayoutService`: `SetVisibleAsync`, `SetLockedAsync`,
  `ReorderAsync`, `FindCurrent`. Mismo lock por sesión (`SemaphoreSlim` por
  `paletteId+paletteVersion`) que `ManufacturingOperationService`, para que
  mutaciones concurrentes no se pisen el avance de versión.
- `LayerLayoutDefaults.Resolve(layerSet, layout)`: punto ÚNICO que resuelve,
  capa por capa, "la última entrada guardada, o si nunca se tocó, los
  defaults ya documentados desde M2.1-S03 (`Visible=true`, `Locked=false`,
  `Order=`posición original)". Reutilizado por `LayerLayoutService` (para
  mutar sobre una base completa) y por `ConsolidatedVectorLayerEndpoints`/
  `LayerLayoutEndpoints` (para leer) — nunca duplicado.

**Deliberadamente NO se agregaron estos 3 campos a `ColorGroup`/
`ColorPaletteVersion`**: esa entidad ya tiene su responsabilidad bien acotada
(detección/reducción de color), y hacerlo hubiera forzado a M2-S01/M2.1-S02 a
saber sobre navegación del Workspace, algo que no les compete.

**Reorder** no usa `int Order` por capa mutado individualmente: el endpoint
recibe la lista `orderedGroupIds` COMPLETA (la nueva secuencia visual entera),
y el servicio valida que sea exactamente una permutación de los `groupId`
vigentes antes de reescribir `Order=índice` — evita huecos/colisiones que un
esquema de "mover 1 capa, reacomodar el resto" tendría que resolver a mano.

`name`/`manufacturingOperation` YA se persistían (rename de M2-S01,
`ManufacturingOperationService` de M2-S07/MVP2) — esta tarjeta solo los
combina en el mismo lugar (`ConsolidatedVectorLayerEndpoints`, que ya lo
hacía) junto con `order`/`visible`/`locked` ahora REALES.

### Endpoints nuevos (`LayerLayoutEndpoints.cs`)

- `POST .../color-palette/{paletteId}/layers/{groupId}/visibility` — `{ visible }`
- `POST .../color-palette/{paletteId}/layers/{groupId}/lock` — `{ locked }`
- `POST .../color-palette/{paletteId}/layers/reorder` — `{ orderedGroupIds }`
- `GET .../color-palette/{paletteId}/layers/layout` — layout completo vigente

Los 4 responden `LayerLayoutSetResponse` (todas las entradas del conjunto
vigente, con defaults para las que nunca se tocaron).

`ConsolidatedVectorLayerEndpoints` (M2.1-S03/S04) se extendió para inyectar
`ILayerLayoutService` y exponer `Visible`/`Locked`/`Order` REALES en vez de
los `true`/`false`/índice hardcodeados que tenía desde M2.1-S03 (ese mismo
endpoint documentaba explícitamente "la persistencia interactiva de esos 3
campos es de M2.1-S07" — esta tarjeta cierra ese pendiente).

## Efímero vs. persistente (decisión documentada, spec.md la dejaba abierta)

**Persistente** (backend, sobrevive a un reload): `order`, `visible` (Eye),
`locked` (Lock), `name`, `manufacturingOperation`.

**Efímero** (solo sesión del Workspace, en `useVectorDocument`/`useLaserWarnings`,
nunca viaja al backend):
- `selectedGroupId` — qué capa se inspecciona ahora (ya era así desde M2.1-S04/S06).
- `isolatedGroupId` — overlay de "Isolate", ver más abajo.
- `selectedPathKeys` — selección múltiple de "Select All in Layer".
- Resultados del Laser Checker (`useLaserWarnings`) — ver sección dedicada.

### Isolate/Show All: reinterpretados como overlay de VISTA puro

Spec.md, Comportamiento: *"Isolate → mostrar solo selección **sin borrar
estados**"*. Antes de esta tarjeta (M2.1-S04), Isolate mutaba directamente el
único `visibility` record que YA era efímero — "sin borrar estados" no
significaba mucho porque no había nada persistente que borrar. Ahora que
`visible` (Eye) es REAL y persistido, si Isolate siguiera sobrescribiendo
`visible=false` de todas las demás capas, "Show All" tendría que decidir
entre (a) restaurar todo a `true` (perdiendo el Eye manual que el usuario
había apagado antes de aislar) o (b) no hacer nada (dejando "Show All" sin
sentido). Ninguna de las dos cumple "sin borrar estados" literalmente.

**Decisión**: `isolate`/`showAll` NUNCA llaman a la Web API. `isolatedGroupId`
es un overlay de sesión en `useVectorDocument`: mientras esté activo, la
`visibility` efectiva que consumen todos los paneles es `{ [isolatedGroupId]:
true, resto: false }`, calculada por encima de `document.layers[].visible`
(el Eye persistido) sin tocarlo. `showAll()` simplemente limpia el overlay
(`isolatedGroupId = null`), revelando el Eye real de cada capa tal como
estaba antes de aislar — reversión manual trivial, sin pila de Undo (ver
sección Undo).

### Warnings láser: cache de sesión, nunca dispara un check automático

`Vectorify.Api.Checking.CheckService` es, por diseño desde M1-S08, de SOLO
LECTURA: no persiste absolutamente nada (su propio docstring: *"sin caché ni
versionado, cada llamada vuelve a analizar el SVG desde cero"*). Por lo
tanto "el último resultado ya disponible" (spec.md) no puede vivir en el
backend — solo puede vivir del lado del cliente, y solo mientras dure la
sesión del Workspace.

`frontend/src/hooks/useLaserWarnings.ts` (nuevo): cache `Record<groupId,
{status, result, errorMessage}>`. `run(groupId, vectorId)` llama a
`POST .../check` (reusa `runPathCheck`/`checkApi.ts` de M1-S08 tal cual,
`sourceKind: "vector"`) **solo cuando el usuario pulsa "Ejecutar Laser
Checker"** en el Inspector — nunca al cargar el documento ni al cambiar de
capa seleccionada. Si no hay nada cacheado para esa capa, el Inspector
muestra un estado vacío honesto ("Todavía no se ejecutó el Laser Checker
para esta capa") en vez de simular un resultado o dispararlo solo.

## Lock (concepto nuevo)

Persistido igual que `visible` (mismo sidecar, mismo patrón optimista+rollback
del lado del cliente). Reglas cumplidas:

- Togglear Lock NUNCA afecta `Visible`/`Order` de esa capa ni de ninguna otra
  (`LayerLayoutService.MutateEntryAsync` solo reemplaza la entrada tocada,
  preserva las demás intactas — test explícito
  `SetLockedAsync_TogglingLock_DoesNotAffectVisibleOrOrderOfTheSameLayer`).
- Una capa bloqueada sigue siendo visible/seleccionable/inspeccionable: Eye,
  Isolate, Select All y el Inspector no consultan `locked` en ningún punto de
  su lógica — solo `VectorCanvas` lo usa, y solo para geometría.
- **Efecto en el Canvas**: dado que MVP3 todavía no agregó ninguna
  herramienta real de transform/drag de paths (fuera de alcance de esta
  tarjeta — ver "Fuera de alcance"), hoy no existe ninguna mutación de
  geometría que bloquear. El Canvas (`VectorCanvas.tsx`) igual refleja el
  dato de punta a punta: cada `<Group>` de Konva de una capa bloqueada recibe
  `draggable={false}` explícito (documentando la intención, listo para que
  MVP3 lo lea) y **nunca** toca `listening` (que sigue en su default `true`)
  — Select/Select All siguen funcionando exactamente igual sobre una capa
  bloqueada. Este alcance concreto (bloqueo "de punta a punta" pero sin una
  interacción de mutación real que gatear todavía) está documentado acá como
  la interpretación resuelta de la ambigüedad.
  Cobertura de test: a nivel de hook (`toggleLocked` no afecta
  `toggleVisibility`/`isolate`/`selectAllInLayer` de la misma capa) y a nivel
  de `EditorLayersPanel`/`InspectorPanel` (Eye y Select siguen habilitados
  con la capa bloqueada). No se agregó un test de interacción Konva
  pixel-a-pixel porque no hay ninguna interacción de mutación real que
  simular todavía — se documenta acá en vez de forzar un test sintético.

## Select All in Layer (primera selección múltiple real)

Acotada a "todos los paths de UN layer", según lo que dejaba abierto spec.md
("el implementador decide cómo se ve"). Diseño:

- `useVectorDocument.selectAllInLayer(groupId)` genera claves
  `${groupId}:${pathIndex}` para `0..pathCount-1` (usa `pathCount`, YA
  conocido desde el consolidado — M1-S05/M2.1-S04 — sin depender de que el
  SVG ya haya terminado de cargar/parsear en el Canvas) y las guarda en
  `selectedPathKeys` (un `Set<string>`, efímero). También fija
  `selectedGroupId = groupId` (Select All implica, como mínimo, seleccionar
  esa capa).
- `selectGroup(groupId)` (selección simple, ya existente) SIEMPRE limpia
  `selectedPathKeys` — un cambio de selección de capa descarta cualquier
  selección múltiple anterior, evitando estados ambiguos ("¿la selección
  múltiple es de la capa vieja o la nueva?").
- `VectorCanvas` recibe `selectedPathKeys` (prop opcional) y resalta con un
  color distinto (`#f5a623`, naranja) cada `<Path>` cuya clave esté en el
  set — visualmente distinto del resaltado de "capa seleccionada" (`#3a5cf5`,
  azul).
- Expuesto en el Inspector ("Seleccionar todo en la capa") según lo pedido
  explícitamente por spec.md ("acciones Isolate/Select All directamente" en
  el Inspector).

Test de cobertura: a nivel de hook (`selectAllInLayer` genera exactamente
`pathCount` claves, `selectGroup` las limpia) — deliberadamente NO se probó
con clicks simulados sobre el `<canvas>` real de Konva/jsdom: la geometría
seleccionable depende de un fetch+parseo asíncrono del SVG que ya estaba
fuera del alcance de los tests existentes de `VectorCanvas.test.tsx` (esos
tests tampoco simulan clicks sobre un `<Path>` real, solo sobre el
contenedor). El contrato de dominio (qué queda seleccionado) es lo que
importa y es lo que se prueba.

## Drag & Drop para reordenar

`EditorLayersPanel` usa Drag & Drop NATIVO de HTML5 (`draggable`,
`onDragStart`/`onDragOver`/`onDrop`/`onDragEnd`) — sin agregar ninguna
librería nueva (justificación de "no agregar dependencias que el diseño no
requiere"). Al soltar una fila sobre otra, calcula el nuevo orden completo
(`groupId` de origen insertado inmediatamente antes del `groupId` destino) y
llama a `onReorder(orderedGroupIds)` → `useVectorDocument.reorderLayers`.

**Nunca toca geometría**: `LayerLayoutService.ReorderAsync` solo reescribe
`Order` de cada `LayerLayoutEntry`; el `VectorLayerSetVersion` (que contiene
`VectorId`/SVG de cada capa) ni se lee para escritura ni se regenera — el
mismo objeto de siempre se devuelve como parte del resultado. Test explícito
en backend (`ReorderAsync_PersistsNewOrderAndNeverMutatesTheVectorLayerSetOrAnyVectorId`)
y en frontend (`useVectorDocument.test.ts`, "Reorder ... preserva el VectorId
de cada capa") comparan el `VectorId` de cada capa antes/después del reorder.

## Sincronización palette↔layer (reverificada)

`selectedGroupId` sigue siendo el único estado compartido entre
`PaletteBar`/`EditorLayersPanel`/`VectorCanvas`/`InspectorPanel` (M2.1-S04/S06,
sin cambios de contrato) — reverificado con los tests existentes de
`EditorShell.test.tsx` ("seleccionar una capa en Layers se refleja en el
Inspector y en la Paleta"), que siguen pasando sin modificación.

## Múltiples layers del mismo color

Verificado: **no es posible por construcción** en el dominio actual. Cada
`ColorGroup` (`backend/Vectorify.Api/ColorPalette/ColorGroup.cs`) proviene de
clusters de color ÚNICOS detectados por Python (`RawGroupIds` disjuntos entre
grupos); `MergeAsync` reduce N grupos a 1 (nunca duplica); `UnmergeAsync`
restaura exactamente los grupos previos (tampoco duplica). No existe ninguna
operación que produzca 2 `VectorLayer` con el mismo `ColorHex` dentro de la
misma paleta+versión confirmada. Documentado acá, sin forzar un caso
artificial (tal como permite spec.md, "Ambigüedades detectadas").

## Auditoría "color no como identificador primario"

Grep dirigido sobre backend (`ColorHex ==`, comparaciones/`GroupBy` por
color) y frontend (`colorHex ===`, indexado por color) en toda la lógica de
selección/persistencia: **ningún hallazgo**. Todo el pipeline (viejo y
nuevo) usa `groupId`/`GroupId` como clave — `colorHex`/`fill` solo se leen
para pintar swatches/SVG. No hizo falta ninguna corrección.

## Undo "cuando corresponda"

Sin pila de Undo/Redo dedicada (siguen deshabilitados en el header, decisión
de M2.1-S06). Cada acción de esta tarjeta es manualmente reversible con la UI
ya existente:
- Eye: click de nuevo → vuelve al valor anterior (persiste igual que el
  primer toggle).
- Lock: click de nuevo → desbloquea.
- Reorder: volver a arrastrar a la posición original.
- Rename: volver a renombrar (ya existía, sin cambios).
- Isolate: "Show All" (ni siquiera hace falta un "undo": es efímero).

## Tests

- **Backend** (`Vectorify.Api.Tests/LayerLayout/`): `LayerLayoutServiceTests`
  (validación, persistencia, independencia Visible/Locked/Order, reorder +
  no-mutación de geometría, reorder inválido, sin migración automática entre
  `PaletteVersion`, defaults) + `PersistentLayerLayoutVersionRegistryTests`
  (sidecar en disco, rehidratación). 651 tests totales en verde (previo:
  ~618; ~33 nuevos entre backend).
- **Frontend** (`useVectorDocument.test.ts`): visibilidad persistida +
  Isolate/Show All efímeros, Lock persistido e independiente, Reorder
  persistido + preserva `VectorId`, Select All in Layer, y un bloque
  dedicado de **reload** que recarga el documento desde cero contra una
  respuesta consolidada con `order`/`visible`/`locked` YA persistidos
  distintos de los defaults, confirmando que sobreviven.
- **Frontend** (`EditorLayersPanel.test.tsx`): Lock (candado abierto/cerrado,
  Eye/Select siguen habilitados con la capa bloqueada) + Drag & Drop
  (soltar sobre otra fila llama a `onReorder` con el orden completo
  correcto; soltar sobre sí misma no llama a nada).
- **Frontend** (`InspectorPanel.test.tsx`): Select All in Layer, Lock (con
  Aislar igual disponible), y los 4 estados de warnings láser (vacío
  honesto + botón, corriendo, listo sin re-disparar, error + reintentar).

## Archivos

### Backend — nuevos
- `backend/Vectorify.Api/LayerLayout/LayerLayoutEntry.cs`
- `backend/Vectorify.Api/LayerLayout/LayerLayoutSetVersion.cs`
- `backend/Vectorify.Api/LayerLayout/ILayerLayoutVersionRegistry.cs`
- `backend/Vectorify.Api/LayerLayout/InMemoryLayerLayoutVersionRegistry.cs`
- `backend/Vectorify.Api/LayerLayout/PersistentLayerLayoutVersionRegistry.cs`
- `backend/Vectorify.Api/LayerLayout/LayerLayoutDefaults.cs`
- `backend/Vectorify.Api/LayerLayout/LayerLayoutResult.cs`
- `backend/Vectorify.Api/LayerLayout/ILayerLayoutService.cs`
- `backend/Vectorify.Api/LayerLayout/LayerLayoutService.cs`
- `backend/Vectorify.Api/Options/LayerLayoutRegistryOptions.cs`
- `backend/Vectorify.Api/Contracts/LayerLayoutResponse.cs`
- `backend/Vectorify.Api/Contracts/LayerLayoutRequests.cs`
- `backend/Vectorify.Api/Endpoints/LayerLayoutEndpoints.cs`
- `backend/Vectorify.Api.Tests/LayerLayout/LayerLayoutServiceTests.cs`
- `backend/Vectorify.Api.Tests/LayerLayout/PersistentLayerLayoutVersionRegistryTests.cs`

### Backend — modificados
- `backend/Vectorify.Api/Endpoints/ConsolidatedVectorLayerEndpoints.cs` (inyecta `ILayerLayoutService`, expone Visible/Locked/Order reales)
- `backend/Vectorify.Api/Program.cs` (registro DI + `MapLayerLayoutEndpoints`)

### Frontend — nuevos
- `frontend/src/types/layerLayout.ts`
- `frontend/src/api/layerLayoutApi.ts`
- `frontend/src/hooks/useLaserWarnings.ts`

### Frontend — modificados
- `frontend/src/hooks/useVectorDocument.ts` (persistencia real visible/locked/order, Isolate/Show All efímeros, Select All in Layer)
- `frontend/src/hooks/useVectorDocument.test.ts`
- `frontend/src/components/editor/EditorLayersPanel.tsx` (Lock interactivo, Drag & Drop)
- `frontend/src/components/editor/EditorLayersPanel.test.tsx`
- `frontend/src/components/editor/InspectorPanel.tsx` (Lock, Select All, warnings láser)
- `frontend/src/components/editor/InspectorPanel.test.tsx`
- `frontend/src/components/editor/VectorCanvas.tsx` (selectedPathKeys, Lock → `draggable=false`)
- `frontend/src/components/editor/VectorCanvas.test.tsx` (defaults `visible`/`locked`)
- `frontend/src/components/editor/PaletteBar.test.tsx` (defaults `visible`/`locked`)
- `frontend/src/components/editor/PreviewNavigator.test.tsx` (defaults `visible`/`locked`)
- `frontend/src/components/editor/EditorShell.tsx` (wiring de Lock/Reorder/Select All/warnings láser)
- `frontend/src/components/editor/editor.css` (estilos de drag-over, extras del Inspector)

## Fuera de alcance (sin cambios respecto de tarjetas anteriores)

Crop/Draw/Erase/Boolean/Nodes/Bridges/IA — ninguno tocado.
