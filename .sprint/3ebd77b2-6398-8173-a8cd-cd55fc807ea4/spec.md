# M2.1-S08 · Persistencia del VectorDocument y reapertura
URL: https://app.notion.com/p/3ebd77b263988173a8cdcd55fc807ea4

Octava y última tarjeta de MVP 2.1. A diferencia de las 7 anteriores, esta tarjeta NO fue transcripta de un cuerpo Notion preexistente: se crea ahora porque M2.1-S06 y M2.1-S07 diferían explícitamente "la persistencia real del VectorDocument y reapertura" para "la tarjeta siguiente" (ver comentarios en `frontend/src/hooks/useVectorDocument.ts` y `IMPL.md` de ambas tarjetas), pero esa tarjeta nunca había sido creada en el board. El orquestador la creó recién ahora, después de investigar el estado real del código para definir un alcance concreto y verificado (no adivinado).

Stack: Frontend (ruteo mínimo) + ASP.NET Core (un endpoint de metadata nuevo, mínimo).

## Objetivo (propiedad Notion)
Que reabrir o recargar el navegador dentro de un proyecto con paleta confirmada recupere exactamente el mismo Workspace (paleta, capas, order/visible/locked/name/operation ya persistidos desde M2.1-S07) en vez de perder toda la navegación y volver a la pantalla de Upload en blanco.

## Criterio de aceptación (propiedad Notion)
La URL del Workspace identifica proyecto+imagen+paleta; recargar el navegador en esa URL reconstruye el mismo VectorDocument (paleta, capas y layout persistidos) sin volver a subir la imagen ni re-seleccionar nada manualmente.

## Hallazgo clave (investigación previa del orquestador): NO es un problema de persistencia de datos

Desde M2.1-S07, `order`/`visible`/`locked`/`name`/`manufacturingOperation` de cada capa YA se persisten de punta a punta en el backend (`LayerLayoutService` + `ColorGroup.Name` + `ManufacturingOperationService`). El problema real es de NAVEGACIÓN:

- `frontend/src/App.tsx` guarda TODO el estado de navegación (`activeProject`, `confirmedPalette`, `isWorkspaceOpen`, `selectedLayerGroupId`, etc.) en `useState` de React puro, sin ningún router ni sincronización con la URL.
- `frontend/package.json` NO tiene `react-router` ni ninguna librería de ruteo (verificado, grep directo).
- NO existe ningún endpoint `GET` que devuelva metadata JSON de un proyecto/imagen ya subida (verificado, grep de `MapGet.*projects/{projectId` en `backend/Vectorify.Api/Endpoints/`). Solo existen: `POST /api/v1/projects` (al subir) y `GET .../original` (sirve el binario de la imagen, no JSON).
- Resultado actual: recargar el navegador estando en el Workspace manda de vuelta a la pantalla de Upload en blanco, perdiendo toda la sesión — aunque los datos en el backend (paleta, capas, layout) siguen intactos.

## Alcance

- [ ] **Ruteo mínimo**: la URL debe identificar proyecto+imagen+paleta (y que el Workspace está abierto). El implementador decide y documenta si alcanza con sincronizar manualmente `History.pushState`/`URLSearchParams` (sin agregar dependencias — mismo criterio ya usado en M2.1-S07 para Drag & Drop nativo) o si conviene agregar `react-router` recién acá (primera vez que haría falta en el proyecto). Justificar la elección en IMPL.md.
- [ ] **Nuevo endpoint de metadata** (backend, mínimo): `GET /api/v1/projects/{projectId}/images/{imageId}` (o ruta equivalente) que devuelva el mismo shape que `UploadImageResponse` (filename/width/height/projectId/imageId), para poder reconstruir `activeProject` a partir de una URL sin volver a subir el archivo. 404 honesto si el proyecto/imagen no existe.
- [ ] **Reconstrucción del Workspace al cargar por URL**: si la URL trae projectId/imageId/paletteId válidos, `App.tsx` debe saltar directo al Workspace (reusando `EditorShell`/`useVectorDocument` TAL CUAL, sin modificar su contrato) en vez de arrancar siempre en la pantalla de Upload.
- [ ] **Al abrir el Workspace desde el flujo normal** (botón "Abrir en el Workspace", ya existente desde M2.1-S06), la URL debe actualizarse para reflejar projectId/imageId/paletteId — así un reload inmediatamente después también reconstruye correctamente (no solo un deep-link pegado manualmente).
- [ ] **Estados vacíos honestos**: proyecto/imagen/paleta inexistente, o paleta encontrada pero no confirmada, en la URL → mismo criterio que `VectorDocumentEmptyReason` ya existente (nunca un crash, nunca datos simulados).

## Fuera de alcance (explícito)

- **Pantalla "Mis Proyectos"** (listar/buscar/ordenar/duplicar/eliminar entre 50+ proyectos): es una tarjeta aparte ya existente en el board, `M2.2-S08 · Pantalla Mis Proyectos` (otro milestone, MVP 2.2). Esta tarjeta NO construye un browser de proyectos — solo hace que la URL en la que ya estás sobreviva a un reload.
- **Ninguna entidad `VectorDocument` nueva en el backend**: decisión ya tomada en M2.1-S06 y reconfirmada en M2.1-S07 — la "persistencia" de esta tarjeta es de NAVEGACIÓN (qué URL reconstruye qué estado del lado del cliente), no de datos de dominio (esos ya persisten desde tarjetas anteriores).
- **Historial de "proyectos recientes"** ni ningún tipo de lista — eso también es M2.2-S08.
- El flujo clásico de abajo (Upload → ColorPalettePanel → LayersPanel → Preprocess → ... → Export, todo MVP1/MVP2) no se toca ni se re-rutea — solo el Workspace (M2.1-S06) gana una URL propia.

## Tests

- Recargar el navegador (simulado en tests: remontar el árbol de React con la URL ya seteada, sin pasar props manualmente — ej. `window.history.pushState` + remount) estando en el Workspace reconstruye paleta + capas + layout (order/visible/locked/name/operation) idénticos a los que había antes de recargar.
- Abrir el Workspace desde el flujo normal actualiza la URL (verificable sin recargar).
- Proyecto/imagen inexistente en la URL → 404 del nuevo endpoint de metadata, manejado como estado vacío honesto en el frontend, nunca un crash.
- Paleta en la URL que existe pero no está confirmada → mismo `emptyReason: "palette_not_confirmed"` ya existente, reusado tal cual.
- Backend: test del nuevo endpoint de metadata (200 con shape correcto, 404 si no existe).

## Definition of Done
Recargar el navegador estando en el Workspace de un proyecto con paleta confirmada reconstruye exactamente la misma sesión, sin perder navegación ni volver a subir nada.

## Referencias visuales / de marca
No aplica — esta tarjeta es de infraestructura de navegación, no cambia ningún componente visual existente.

## Umbrales de calidad
Mismo estándar ya usado en MVP2.1: `npm run build` (no solo `npm test`) sin errores, los test suites completos en verde (dotnet build/test, pytest, npm test, npm run build).

## Ambigüedades detectadas
- **Ruteo con librería vs. manual**: no especificado — el implementador decide y justifica (ver "Alcance" arriba). Recomendación del orquestador: dado que hoy no hay NINGUNA necesidad de rutas anidadas/nested routing (es un solo estado "Workspace abierto con estos 3 ids" vs. "flujo clásico"), un manejo manual con `URLSearchParams`/`history.pushState` puede alcanzar sin agregar una dependencia nueva — pero si el implementador ve que esto crece en complejidad, agregar `react-router` está permitido con justificación documentada.
- **Nombre/forma exacta del endpoint de metadata**: no especificado — el implementador elige una ruta RESTful coherente con las ya existentes (ver ejemplos de endpoints hermanos en `backend/Vectorify.Api/Endpoints/ProjectEndpoints.cs`) y lo documenta.
- Ninguna otra ambigüedad bloqueante.

## Nota de orquestación
Octava y ÚLTIMA tarjeta de MVP 2.1, corrida en modo autónomo (mismo criterio ya establecido para S01-S07). A diferencia de las anteriores, esta tarjeta fue creada por el propio orquestador (con confirmación del usuario) en vez de transcribirse de un cuerpo Notion preexistente — ver "Hallazgo clave" arriba para la justificación completa del alcance. Recordatorio: correr `npm run build` además de `npm test`.
