# M2.1-S07 — Ronda de fix 1: Rename y cambio de operación en el Workspace

Bug reportado por el usuario: el Workspace no ofrecía ninguna acción para renombrar un Layer ni
cambiar su operación CUT/ENGRAVE/IGNORE — Layers e Inspector solo mostraban ambos campos como
texto de solo lectura, pese a que spec.md pedía explícitamente ambos comportamientos.

## Qué se conectó

### Cambio de operación CUT/ENGRAVE/IGNORE — funciona de punta a punta

- `EditorShell.tsx` ahora monta `useManufacturingOperations(projectId, imageId, paletteId,
  document?.layerSetId ?? null)` — el mismo hook que ya usa el flujo clásico
  (`LayersPanel.tsx`) — y pasa `operations`/`assign` (como `onChangeOperation`)/
  `mutatingGroupId` a `EditorLayersPanel` e `InspectorPanel`.
- `EditorLayersPanel.tsx`: cada fila reemplaza el `<span>` de solo lectura de la operación por
  un `<select>` idéntico al de `LayerList.tsx` (mismas 3 opciones + "Sin asignar" deshabilitada/
  oculta), deshabilitado si la capa está bloqueada (Lock) o si esa fila tiene una request de
  operación en curso (`mutatingGroupId`).
- `LayerInfoPanel.tsx` (compartido con el flujo clásico) suma `onChangeOperation`/
  `operationDisabled` como props **opcionales**: si se proveen, la fila "Operación de
  fabricación" del Inspector pasa de `<dd>` de solo lectura a `<select>`; si no se proveen (el
  flujo clásico, `LayersPanel.tsx`, no las pasa), se comporta exactamente igual que antes — ese
  flujo ya tiene su propio selector por fila en `LayerList`, no duplica la UI.
- `InspectorPanel.tsx` pasa esas props resolviendo el valor EFECTIVO de la operación como
  `operations[groupId]?.operation ?? selectedLayer.manufacturingOperation` (prioriza el estado
  ya vivo del hook tras una asignación reciente, cae al valor del consolidado mientras el hook
  todavía no resolvió su fetch inicial) — mismo criterio en `EditorLayersPanel`.

Este endpoint (`POST .../layers/{groupId}/operation`, `ManufacturingOperationService`) **no**
tiene ninguna precondición de "paleta confirmada" — es un sidecar independiente de
`ColorPaletteService`, pensado explícitamente para operar sobre paletas ya confirmadas (que es
exactamente el estado en que siempre está el Workspace). Verificado con un test de wiring
end-to-end en `EditorShell.test.tsx` que confirma el POST real con el `groupId`/`operation`
correctos y que el `<select>` refleja el valor autoritativo de la respuesta (no solo un cambio
optimista local).

### Rename — UI conectada, pero con una limitación de backend preexistente (ver abajo)

- `useVectorDocument.ts` suma `renameLayer(groupId, name)`: mismo patrón optimista con rollback
  silencioso en caso de error que ya usan `toggleVisibility`/`toggleLocked` en ese mismo hook —
  reusa EXACTAMENTE `renameColorPaletteGroup` (`colorPaletteApi.ts`, endpoint de M2-S01), no se
  creó ningún endpoint nuevo.
- `EditorLayersPanel.tsx`: el nombre de cada fila pasa de `<span>` a un `<input>` con el mismo
  patrón de edición inline que `ColorSwatchRow` (`ColorSwatchList.tsx`): borrador local, commit a
  blur/Enter, resincronización durante el render si el nombre cambia por fuera. Se extrajo un
  subcomponente `EditorLayerRow` (antes el `.map()` renderizaba el `<li>` inline) porque cada fila
  necesita su propio estado local de edición — hook de React, no se puede declarar dentro de un
  callback de `.map()`.
- `LayerInfoPanel.tsx` suma `onRename`/`renameDisabled` como props **opcionales**, mismo criterio
  que `onChangeOperation` arriba: si se proveen (Inspector del Workspace), el título pasa de
  `<span>` a `<input>` editable; si no (flujo clásico), sigue siendo el `<span>` de siempre — ahí
  renombrar sigue siendo responsabilidad exclusiva de `ColorSwatchList`, sin cambios.

**Limitación de backend descubierta durante esta ronda (no introducida por este fix, ya
preexistía)**: `ColorPaletteService.RenameAsync` rechaza con 409 `palette_confirmed` en cuanto la
paleta está confirmada (`ColorPaletteServiceTests.RenameAsync_OnAConfirmedPalette_ReturnsConflict`,
test ya existente y deliberado, no un bug). El Workspace **siempre** opera sobre una paleta
confirmada — es precondición dura de `VectorLayerService.GenerateLayersAsync` (sin capas generadas
no hay nada que mostrar en el Workspace) y el propio `EditorShell.tsx` ya lo documentaba así antes
de esta ronda ("Sesión de paleta YA CONFIRMADA -- precondición para abrir el Workspace"). El mismo
patrón aplica al flujo clásico: `LayersPanel.tsx` también exige paleta confirmada como precondición,
y por eso la clásica `ColorSwatchList` deshabilita su propio input de rename en cuanto la paleta se
confirma (`ColorPalettePanel.tsx`, `canEdit = !isConfirmed`).

En otras palabras: el spec.md original de esta tarjeta (línea "Rename... `VectorLayer.Name` viene
directo de `ColorGroup.Name`, renombrar el grupo YA renombra la capa, reusar tal cual") daba por
sentado algo que el código real no hace — ni siquiera en el flujo clásico. `VectorLayer.Name` se
CAPTURA una sola vez al generar el conjunto de capas (`VectorLayerService.cs`, línea ~219) y nunca
se vuelve a sincronizar con `ColorGroup.Name`; y el propio endpoint de rename bloquea la edición
una vez confirmada la paleta, que es la única situación en la que el Workspace (o `LayersPanel`
clásico) tienen algo que mostrar.

**Qué se entregó de todos modos, y por qué**: la instrucción explícita de esta ronda fue "puramente
wiring de UI, reusar EXACTAMENTE `renameColorPaletteGroup`, no crear ningún endpoint nuevo". Dado
ese límite explícito, conecté el wiring completo y correcto (UI, hook, commit a blur/Enter, Lock
deshabilita el control, tests de que llama al handler correcto) exactamente como se pidió — pero
en la práctica, hoy, `renameLayer` siempre hace rollback (la fila vuelve a mostrar el nombre
anterior tras un instante) porque la Web API siempre responde 409. Esto es honesto y consistente
con el resto del hook (ningún otro toggle de esta tarjeta muestra un mensaje de error tampoco, tal
como ya estaba documentado para `toggleVisibility`/`toggleLocked`) pero **no resuelve de verdad el
Rename para el usuario final**. Para que Rename funcione de punta a punta en el Workspace hace
falta una decisión de backend fuera del alcance autorizado de esta ronda — dos caminos razonables,
ninguno implementado acá:

1. Relajar `ColorPaletteService.RenameAsync` para permitir renombrar un grupo aun con la paleta
   confirmada (con las implicancias de versión/no-regeneración de capas que eso requiera pensar).
2. Agregar `Name` al sidecar `LayerLayout` (mismo patrón que `order`/`visible`/`locked`, ya
   pensado para mutarse post-confirmación) y que el Workspace lo use en vez de
   `ColorPaletteService`, dejando el rename de M2-S01 exactamente como está (solo pre-confirmación,
   solo para la paleta en sí).

Recomiendo tratar esto como una tarjeta de seguimiento explícita en vez de asumir que esta ronda
lo resolvió.

## Decisión de Lock

Una capa bloqueada (`layer.locked`) deshabilita tanto el `<input>` de rename como el `<select>` de
operación, en `EditorLayersPanel` e `InspectorPanel`/`LayerInfoPanel` — mismo criterio `disabled`+
`title` explicativo que ya usan el resto de los controles de esta tarjeta (Eye/Select siguen
habilitados, Lock nunca los afecta). El spec.md original solo hablaba de geometría/Canvas para
"no editable", pero dado que esta ronda agrega edición real de metadata (nombre/operación), se
extendió la misma regla por consistencia — documentado acá según lo pedido.

## Archivos modificados

- `frontend/src/components/editor/EditorLayersPanel.tsx` — rename inline + `<select>` de
  operación por fila, ambos deshabilitados si Lock; se extrajo `EditorLayerRow`.
- `frontend/src/components/editor/InspectorPanel.tsx` — pasa `onRename`/`operations`/
  `onChangeOperation`/`mutatingGroupId` a `LayerInfoPanel`.
- `frontend/src/components/layers/LayerInfoPanel.tsx` — `onRename`/`renameDisabled`/
  `onChangeOperation`/`operationDisabled` OPCIONALES; sin cambios de comportamiento para el flujo
  clásico (`LayersPanel.tsx`), que no las pasa.
- `frontend/src/components/editor/EditorShell.tsx` — monta `useManufacturingOperations` y pasa
  `renameLayer` (de `useVectorDocument`) a ambos paneles.
- `frontend/src/hooks/useVectorDocument.ts` — `renameLayer(groupId, name)`, mismo patrón
  optimista+rollback que `toggleLocked`/`toggleVisibility`.
- `frontend/src/components/editor/editor.css`, `frontend/src/App.css` — estilos de los nuevos
  `<input>`/`<select>` inline.
- Tests actualizados/sumados: `EditorLayersPanel.test.tsx`, `InspectorPanel.test.tsx`,
  `EditorShell.test.tsx` (wiring real de `useManufacturingOperations`, POST verificado),
  `App.workspaceDeepLink.test.tsx` (ajustado: el nombre de una capa ahora es un `<input>`, se
  verifica por `getByDisplayValue` en vez de `getByText`).

## Verificación (ronda de fix 1)

- `dotnet build` (backend): 0 errores (sin cambios de backend en esta ronda).
- `dotnet test` (backend): 653 passed, 0 failed.
- `pytest` (python-engine): 400 passed.
- `npm test -- --run` (frontend): 304 passed (36 archivos).
- `npm run build` (frontend): `tsc -b && vite build`, 0 errores de TypeScript.
- `npm run lint` (frontend): sin errores nuevos (warnings preexistentes ajenos a esta ronda).

---

# Ronda de fix 2 — Rename persiste de verdad (Camino B: sidecar `LayerLayout`)

La ronda 1 dejó a `renameLayer` reusando `renameColorPaletteGroup` (`ColorPaletteService.RenameAsync`,
M2-S01) tal como pedía la instrucción explícita de esa ronda ("puramente wiring de UI, no crear
ningún endpoint nuevo") — pero documentó que esa llamada **siempre** hace rollback en el Workspace,
porque `ColorPaletteService.RenameAsync` rechaza con 409 `palette_confirmed` en cuanto la paleta
está confirmada, que es SIEMPRE el caso ahí. Esta ronda resuelve esa limitación.

## Por qué se descartó el Camino A (relajar la gate de `ColorPaletteService.RenameAsync`)

`ColorPaletteService.RenameAsync` bloquea ediciones de `ColorGroup` (nombre incluido) una vez
confirmada la paleta porque esa confirmación es el punto en el que `VectorLayerService` CAPTURA un
snapshot inmutable (`VectorLayer.Name`, entre otros campos) para generar las capas — y, más en
general, porque `ColorPaletteVersion`/`ColorGroup` son el resultado de la DETECCIÓN/reducción de
color (M2-S01/M2-S02), no metadata de navegación del Workspace. Relajar esa gate para permitir
renombrar post-confirmación habría mezclado dos responsabilidades distintas (inmutabilidad de un
resultado de detección vs. metadata editable de UI) y habría requerido pensar de cero qué pasa con
`VectorLayerSetVersion`/regeneración de capas en cada rename — un cambio bastante más grande y
arriesgado que el problema que hay que resolver. Se descartó explícitamente.

## Camino B implementado: `Name` en el sidecar `LayerLayout`

Mismo criterio que `order`/`visible`/`locked`, ya pensados desde M2.1-S07 para mutarse
post-confirmación sin tocar `ColorPaletteService`/`ColorGroup`/`ColorPaletteVersion` — un nombre
editable post-generación es exactamente ese tipo de dato: metadata pura de navegación/edición del
Workspace, nunca geometría, nunca una referencia a `VectorId`.

### Backend

- `LayerLayout/LayerLayoutEntry.cs` — suma `string? Name` al record. `null` = "sin override, usar
  el nombre original de la capa (`VectorLayer.Name`)" — nunca se inventa un valor acá.
- `LayerLayout/LayerLayoutDefaults.cs` — el default de una capa nunca tocada sigue siendo
  `Name: null` (el fallback es responsabilidad del consumidor, no de este helper).
- `LayerLayout/ILayerLayoutService.cs` / `LayerLayoutService.cs` — `SetNameAsync`, mismo patrón
  EXACTO que `SetVisibleAsync`/`SetLockedAsync` (`MutateEntryAsync` con
  `entry => entry with { Name = trimmedName }`): valida `name` no vacío/solo espacios
  (`ValidationFailed("invalid_parameters", ...)`, mismo código que
  `ColorPaletteService.RenameAsync`) y trimea antes de guardar.
- `Contracts/LayerLayoutRequests.cs` — `SetLayerNameRequest(string Name)`.
- `Contracts/LayerLayoutResponse.cs` — `LayerLayoutEntryPayload` suma `string? Name`.
- `Endpoints/LayerLayoutEndpoints.cs` — `POST .../layers/{groupId}/rename`, mismo patrón que
  `/lock`; `ToResponse` propaga `Name`.
- `Endpoints/ConsolidatedVectorLayerEndpoints.cs` — `ToPayload`: `Name: layout.Name ?? layer.Name`
  (el nombre EFECTIVO es el override del sidecar si existe, o el snapshot original de
  `VectorLayer.Name` — mismo criterio ya usado para `Visible`/`Locked`/`Order`). `ColorGroup.Name`/
  `VectorLayer.Name`/`ColorPaletteService.RenameAsync` quedan intactos, sin ningún cambio.
- Tests nuevos: `LayerLayoutServiceTests` (persiste y se relee, trimea, rechaza vacío/null/
  whitespace, no afecta Visible/Locked/Order de la misma capa ni de otras, 404 si el groupId no
  existe, default `Name: null` para una capa nunca tocada) y
  `ConsolidatedVectorLayerEndpointsTests.GetConsolidated_AfterRenamingALayerViaTheLayoutSidecar_ReflectsTheOverrideName`
  (flujo HTTP real: upload → detect → confirm → layers → rename vía el sidecar → consolidated
  refleja el nombre nuevo, contra una paleta CONFIRMADA de verdad, no simulada).

### Frontend

- `types/layerLayout.ts` — `LayerLayoutEntryPayload.name: string | null`.
- `api/layerLayoutApi.ts` — `setLayerName(...)`, mismo patrón que `setLayerLocked`/
  `setLayerVisible`, pega a `.../rename`.
- `hooks/useVectorDocument.ts`:
  - `renameLayer` ahora llama a `setLayerName` (sidecar nuevo) en vez de
    `renameColorPaletteGroup` — mismo patrón optimista+rollback que `toggleLocked`/
    `toggleVisibility`, y en éxito aplica `applyLayoutEntries` con la respuesta autoritativa.
  - `applyLayoutEntries` ahora también copia `name` de cada entry (con fallback al nombre en
    memoria si `entry.name` es `null`, nunca pisa con un valor inventado).
  - `toDocument`: el `name` de cada capa ahora prioriza `info?.name` (del consolidado, que ya
    expone el nombre EFECTIVO resuelto por el backend) sobre `layer.name` (el snapshot crudo de
    `VectorLayer.Name`) — mismo criterio ya usado para `visible`/`locked`/`order`.
  - Docstring de `renameLayer` actualizado: ya no documenta la limitación de la ronda 1 (409
    siempre), documenta la persistencia real vía el sidecar.
- Tests: `useVectorDocument.test.ts` suma un bloque "Rename" (persiste vía `setLayerName`, NUNCA
  pega al endpoint clásico `.../color-palette/{paletteId}/rename`, no afecta GroupId/Visible/
  Locked/Order de la capa renombrada ni de otras, rollback si la Web API falla) y un bloque de
  reload que confirma que el nombre override sobrevive a un reload/remount (mismo patrón que el
  bloque ya existente para order/visible/locked). Mocks de `consolidatedResponse`/`layoutResponse`
  actualizados con el campo `name` nuevo.

**Reglas respetadas**: `ColorPaletteService.RenameAsync`/su gate de confirmación quedan intactas
(protegen la inmutabilidad de la detección de color); `renameColorPaletteGroup`/`ColorSwatchList.tsx`
(el flujo clásico pre-confirmación) siguen exactamente igual, sin ningún cambio; el `GroupId` nunca
cambia — el nombre es pura metadata, mismo criterio que el resto del sidecar; Lock sigue
deshabilitando el input de rename en la UI (ronda 1, sin tocar), y ahora esa deshabilitación tiene
sentido real porque la persistencia de fondo funciona de verdad.

**Confirmación explícita**: Rename ahora persiste de punta a punta contra una paleta CONFIRMADA —
verificado con un test de integración HTTP real (`ConsolidatedVectorLayerEndpointsTests`, arriba)
que levanta la Web API completa, confirma la paleta, genera capas, renombra vía el endpoint nuevo y
confirma que el consolidado devuelve el nombre nuevo — no solo que la UI/el hook están conectados a
algo; hay persistencia de verdad detrás.

## Verificación (ronda de fix 2)

- `dotnet build` (backend): 0 errores.
- `dotnet test` (backend): 662 passed, 0 failed (653 previos + 9 nuevos de esta ronda).
- `pytest` (python-engine): 400 passed (sin cambios de Python en esta ronda).
- `npm test -- --run` (frontend): 307 passed, 36 archivos (304 previos + 3 nuevos de esta ronda).
- `npm run build` (frontend): `tsc -b && vite build`, 0 errores de TypeScript.
- `npm run lint` (frontend): sin errores nuevos (mismos warnings preexistentes de antes, ajenos a
  esta ronda).
