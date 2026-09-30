# IMPL.md — M2.1-S08 · Persistencia del VectorDocument y reapertura

## Resumen

Octava y última tarjeta de MVP 2.1. Como el hallazgo del spec ya adelantaba,
esto NO era un problema de persistencia de datos de dominio (eso ya estaba
resuelto desde M2.1-S07) sino de navegación: la URL del Workspace no
identificaba proyecto+imagen+paleta, así que un reload volvía siempre a
Upload en blanco aunque el backend tuviera todo intacto. Se resolvió con (1)
un endpoint de metadata nuevo en el backend y (2) ruteo mínimo manual en el
frontend (sin agregar dependencias) que lee/escribe esos 3 ids en la URL y
reconstruye `activeProject`/`confirmedPalette` al montar, reusando
`EditorShell`/`useVectorDocument` tal cual.

## Decisión 1: ruteo manual (`URLSearchParams` + `History.pushState`), sin `react-router`

Se evaluó agregar `react-router` (sería la primera vez en el proyecto) contra
sincronizar la URL a mano. Se eligió la segunda opción, mismo criterio que
M2.1-S07 usó para Drag & Drop nativo (preferir la plataforma antes que una
dependencia nueva cuando el caso de uso es acotado):

- Hoy la app tiene exactamente UN estado de navegación real: "Workspace
  abierto con estos 3 ids" vs. "flujo clásico de Upload hacia abajo". No hay
  rutas anidadas, ni parámetros dinámicos adicionales, ni un layout por ruta
  que justifique el árbol de componentes / matching de rutas que
  `react-router` resuelve.
- El único consumidor de la URL es `App.tsx` (un solo componente): no hay
  necesidad de que `EditorShell` ni ningún panel hijo conozcan la URL — siguen
  recibiendo `projectId`/`imageId`/`paletteId` como props tal cual ya lo
  hacían desde M2.1-S06, sin tocar su contrato.
- El costo de la alternativa manual es bajo y queda contenido en un solo
  archivo puro y testeable (`frontend/src/lib/workspaceLocation.ts`, 4
  funciones: `readWorkspaceLocation`, `buildWorkspaceSearch`,
  `pushWorkspaceLocation`, `clearWorkspaceLocation`), con su propio test
  unitario (`workspaceLocation.test.ts`).

Límite explícito de esta decisión (documentado, no un descuido): no hay
listener de `popstate`, así que el botón "Atrás" del navegador no
sincroniza el estado de React mientras el Workspace está montado (cerrar el
Workspace usa el botón "← Projects" existente, que además limpia la URL). No
estaba en el alcance ni en los tests requeridos, y agregarlo hubiera sido la
primera señal real de que conviene reevaluar `react-router` — que es
exactamente el criterio que dejó planteado el spec para "si esto crece en
complejidad". Si una tarjeta futura agrega rutas anidadas reales (la
pantalla "Mis Proyectos" de M2.2-S08, con su propia URL y navegación entre
proyectos), ahí sí vale la pena reevaluar.

## Decisión 2: forma del endpoint de metadata nuevo

`GET /api/v1/projects/{projectId}/images/{imageId}` (sin el sufijo `/original`
que ya usa el endpoint hermano que sirve el binario) — devuelve el mismo
shape que `UploadImageResponse` (`filename`/`mimeType`/`bytes`/`width`/
`height`/`status`), construido a partir del mismo `ProjectRecord` que ya
guarda `IProjectRegistry` (ningún dato nuevo, solo una lectura adicional).
404 (`not_found`) si `IProjectRegistry.Find` no encuentra el proyecto/imagen
— mismo `ApiErrorResponse` ya usado por el resto de `ProjectEndpoints`.

Se agregó como una ruta hermana dentro de `ProjectEndpoints.cs` (no un
archivo de endpoints nuevo): mismas convenciones ya establecidas ahí
(`IProjectRegistry`, `ApiErrorResponse`, nombres `WithName`/`WithTags`/
`WithSummary`).

Frontend: `getProjectImage(projectId, imageId)` nueva en `projectsApi.ts`
(mismo archivo que ya expone `uploadProjectImage`/`getOriginalImageUrl`),
usando `httpClient.get` (mismo cliente centralizado que el resto del
frontend, sin nada especial).

## Decisión 3: qué hace `App.tsx` al montar, y qué NO duplica

Al montar, `App.tsx` corre un efecto ÚNICO (guardado con un `useRef`, no se
repite en renders posteriores) que lee la URL con `readWorkspaceLocation()`.
Si trae los 3 ids:

1. `getProjectImage(projectId, imageId)` → reconstruye `activeProject`. Si
   falla (404 o red), limpia la URL y muestra un estado vacío honesto (banner
   `role="alert"`) sobre el flujo clásico — nunca un crash, nunca se queda
   "pegado" en un estado de carga infinito.
2. `getColorPalette(projectId, imageId, paletteId)` (GET ya existente,
   mismo que usa `useVectorDocument`) → reconstruye `confirmedPalette`.
   Mismo manejo de error que el paso anterior.
3. Si ambos resuelven, setea `isWorkspaceOpen=true` y el render branch
   YA EXISTENTE de `App.tsx` (`isWorkspaceOpen && activeProject &&
   confirmedPalette`) monta `EditorShell` sin ningún cambio.

Deliberadamente **no se replica** en `App.tsx` la lógica de "¿la paleta está
confirmada?": `confirmedPalette` se setea con la respuesta cruda de
`getColorPalette` tal cual (incluso si `isConfirmed === false`), porque
`EditorShell` → `useVectorDocument` YA vuelve a pedir esa misma paleta al
montar y YA sabe convertir "no confirmada" en
`emptyReason: "palette_not_confirmed"` (existente desde M2.1-S06/S07). Esto
es exactamente lo que pide el spec ("reusar tal cual, sin duplicar esa
lógica acá") — la llamada de `App.tsx` es redundante con la de
`useVectorDocument` en ese caso puntual, aceptado a propósito para no
bifurcar el criterio de "paleta confirmada" en dos lugares.

## Decisión 4: cuándo se escribe la URL desde el flujo normal

`pushWorkspaceLocation(...)` se llama en el `onClick` del botón "Abrir en el
Workspace" (ya existente desde M2.1-S06), ANTES de `setIsWorkspaceOpen(true)`
— no en un efecto posterior — así que un reload inmediatamente después de
abrir el Workspace reconstruye la misma sesión sin depender de que el efecto
de montaje llegue a correr primero. `clearWorkspaceLocation()` se llama en el
`onClose` de `EditorShell` (botón "← Projects"), para que volver al flujo
clásico no deje la URL "pegada" apuntando a un Workspace que ya no está
abierto.

## Tests

- `frontend/src/lib/workspaceLocation.test.ts`: las 4 funciones puras
  (lectura con deep-link parcial/completo, construcción de query string,
  push/clear contra `window.location` real).
- `frontend/src/App.workspaceDeepLink.test.tsx` (nuevo, separado de
  `App.test.tsx` para no mezclar con los tests de diagnóstico del sistema):
  - Reload reconstruye paleta + capas + layout PERSISTIDO no trivial (orden
    invertido, una capa oculta y bloqueada) idéntico antes/después de
    desmontar y volver a montar `<App />` con la URL ya seteada (simulando
    un reload real, sin pasar props a mano).
  - Abrir el Workspace desde el flujo normal (`UploadPanel`/
    `ColorPalettePanel` mockeados con stubs mínimos, ya que su lógica interna
    tiene sus propios tests) actualiza `window.location.search` con los 3
    ids, verificable sin recargar.
  - Proyecto/imagen inexistente en la URL → 404 del endpoint de metadata →
    banner de error honesto, vuelve al flujo clásico, la URL inválida se
    limpia, nunca un crash.
  - Paleta en la URL existente pero no confirmada → mismo
    `emptyReason: "palette_not_confirmed"` ya existente, mostrado dentro del
    propio `EditorShell` (confirma que no se duplicó esa lógica en
    `App.tsx`, ver Decisión 3).
- Backend: `ProjectEndpointsTests` (`GetMetadata_WhenProjectExists_...` /
  `GetMetadata_WhenProjectDoesNotExist_...`) — 200 con el mismo shape que
  `UploadImageResponse`, 404 con `ApiErrorResponse("not_found", ...)`.

## Verificación

- `dotnet build` (backend): 0 errores.
- `dotnet test` (backend): 653 passed, 0 failed.
- `pytest` (python-engine, `.venv` activo): 400 passed, sin tocar Python en
  esta tarjeta (confirmado en verde igual, como pide el spec).
- `npm test -- --run` (frontend): 290 passed (36 test files), incluye los 10
  tests nuevos de esta tarjeta.
- `npm run build` (frontend): `tsc -b && vite build`, 0 errores de
  TypeScript, build completo.
- `npm run lint` (frontend): exit 0. Queda un warning nuevo,
  `react(set-state-in-effect)` en `App.tsx` (el `setDeepLinkStatus("resolving")`
  síncrono al arrancar la resolución del deep-link) — mismo patrón de
  warning que YA existe en 2 archivos del spike `spike-editor-engine/`
  (preexistentes, no tocados por esta tarjeta). Se evaluó extraer la lógica a
  un `useCallback` (mismo patrón que `useVectorDocument.load`) pero oxlint
  sigue el flujo hasta el `setState` igual; no se encontró una
  reestructuración razonable que lo elimine sin violar "un solo efecto de
  montaje, guardado con `useRef`" (ya el patrón más simple disponible). Es un
  warning, no un error — no bloquea `npm run lint` (exit 0) ni ningún otro
  comando de verificación.

## Fuera de alcance (respetado, no tocado)

- Ninguna pantalla "Mis Proyectos" (esa es M2.2-S08).
- Ninguna entidad `VectorDocument` nueva en el backend — la reconstrucción
  sigue siendo 100% agregación del lado del cliente sobre endpoints ya
  existentes (`useVectorDocument`, sin cambios en su contrato).
- El flujo clásico de abajo (Upload → ColorPalettePanel → LayersPanel →
  Preprocess → ... → Export) no se re-ruteó: solo gana un punto de
  lectura/escritura de URL en dos lugares puntuales (montaje, botón "Abrir en
  el Workspace") y uno de limpieza (`onClose` del Workspace).
