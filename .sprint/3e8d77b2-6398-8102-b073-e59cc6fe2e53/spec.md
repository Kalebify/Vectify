# spec.md — M2.2-S05 · Persistencia completa del VectorDocument

## Contexto

Quinta tarjeta de MVP 2.2. Hasta ahora PostgreSQL + Object Storage (M2.2-S01..S04) existen
como infraestructura y como CRUD de `Project`/`Asset`, pero **ningún código escribe todavía
una fila original en `VectorDocument`/`DocumentVersion`/`Layer`/`PaletteColor`** (M2.2-S02) —
la única lectura/escritura real de esas tablas es `ProjectRepository.DuplicateAsync`, que solo
*copia* filas que ya existen. Esta tarjeta es la primera en crear esas filas desde cero: el
objetivo es que el `VectorDocument` completo que hoy vive en memoria del cliente
(`useVectorDocument.ts`, agregado en caliente desde varios endpoints de lectura del flujo
clásico) sobreviva a un **Save** explícito, un cierre de navegador y un reinicio completo de
los containers (`docker compose down && up`), y pueda reabrirse de forma equivalente.

Investigación previa (ver IMPL.md para el detalle completo) confirmó dos huecos
arquitectónicos reales que se resolvieron con el usuario antes de este spec:

## Decisiones arquitectónicas confirmadas

### 1. Puente entre el Project clásico y el Project v2

`/api/v1/projects` (`ProjectRecord`/`IProjectRegistry`, par `ProjectId+ImageId` sin
ownership, sidecar JSON) y `/api/v2/projects` (`Data.Project`, EF Core, con `OwnerId`) son
dos conceptos de dominio distintos que ya coexistían desde M2.2-S03 ("Conflicto real
detectado" documentado en `ProjectV2Endpoints.cs`). El Workspace del editor solo conoce hoy
el triple clásico `(projectId, imageId, paletteId)`.

**Decisión (confirmada por el usuario): auto-crear el `Project` v2 en el primer Save.**
- El botón Guardar, si el Workspace todavía no tiene un `Project.Id` v2 asociado, crea uno
  nuevo (`ownerId` resuelto vía `IUserContext`, `Name` provisto en el request) junto con su
  `VectorDocument`/primera `DocumentVersion`.
- Saves subsiguientes del mismo Workspace reutilizan ese `Project.Id` (el cliente lo guarda
  en el estado del Workspace y lo agrega a la URL — ver "Frontend", sección Reapertura).
- El triple clásico `(projectId, imageId, paletteId)` sigue siendo el insumo de *staging*:
  identifica la sesión de vectorización/paleta en curso, pero deja de ser lo que identifica
  "un proyecto guardado" una vez que existe un `Project.Id` v2 para esa sesión.
- No hay remapeo retroactivo: proyectos clásicos nunca guardados con este mecanismo
  simplemente no tienen `Project.Id` v2 y no aparecen en ningún listado "Mis proyectos"
  (ese listado ya existe desde M2.2-S03, `GET /api/v2/projects`, sin cambios en esta tarjeta).

### 2. Sidecars `LayerLayout`/`ManufacturingOperation` vs. `Data.Layer`

**Decisión (confirmada por el usuario): cutover en el límite del Save, no migración
completa ni dual-write.**
- **Antes de guardar** (staging, sin `Project.Id` v2 todavía): cero cambios de
  comportamiento. Order/visible/locked/name siguen via `LayerLayoutService`/sidecar JSON,
  operación de fabricación sigue via `ManufacturingOperationService`/sidecar JSON, exactamente
  como hoy. `useVectorDocument.ts` no cambia su fuente de verdad en este modo.
- **Save**: lee el estado actual de ambos sidecars (vía los servicios existentes, no
  releyendo el JSON a mano) + el layer set/paleta vigente, y los vuelca como snapshot en
  `Layer`/`PaletteColor` de una `DocumentVersion` nueva. Esta es la ÚNICA vía de escritura a
  esas tablas en esta tarjeta.
- **Después de guardar, mientras el Workspace tiene un `Project.Id` v2 activo**: toda edición
  de order/visible/locked/name/operación pasa a leer/escribir contra la DB (nuevos endpoints,
  ver abajo), no contra los sidecars. Los endpoints viejos de `LayerLayoutEndpoints`/
  `ManufacturingOperationEndpoints` siguen existiendo sin cambios (los sigue usando el modo
  staging), pero el Workspace deja de llamarlos una vez que hay un `Project.Id` v2.
- Los sidecars **no se retiran** ni se tocan en esta tarjeta — coexisten indefinidamente,
  cada uno dueño de su propia fase (staging vs. guardado). La migración completa a `Data.Layer`
  que el docstring de `Data/Layer.cs` dejaba pendiente sigue, explícitamente, fuera de alcance.

## Qué debe persistirse (del criterio de aceptación de la tarjeta)

VectorDocument + versión actual; dimensiones mm; viewBox; SVG canónico; Palette/Color IDs;
Layers con IDs estables, nombre, orden, visible, locked, CUT/ENGRAVE/IGNORE; referencias a
assets; parámetros necesarios para reproducibilidad.

### Geometría: qué se normaliza en tablas vs. qué se guarda como asset

`Data.Layer` (M2.2-S02) **no tiene campo de geometría ni de asset propio** — solo metadata
(`Name`, `Order`, `Visible`, `Locked`, `ManufacturingOperation`, `ColorId`).
`DocumentVersion.SvgAssetId` es un único asset opcional por versión, pensado como snapshot
combinado, no como contenedor de cada layer por separado.

**Decisión de diseño (técnica, no de producto — sigo el mismo criterio "extender, no
reinventar" de M2.2-S04)**: agrego una migración aditiva mínima,
`Layer.SvgAssetId` (`Guid?`, FK a `Asset`, nullable). Cada layer guardado sube su SVG ya
coloreado (el mismo que hoy sirve `ConsolidatedVectorLayerEndpoints`, vía `IFileStorage`) como
un `Asset` propio (`type: "layer-svg"`), reusando `IAssetService`/`AssetKeyFactory` tal cual
existen. Esto evita inventar un formato de SVG compuesto con `<g>` por layer y su lógica de
parseo/split para reabrir — cada layer sigue teniendo su propio SVG real, igual que en el
flujo clásico, solo que ahora vive como `Asset` en vez de en el storage key ad-hoc de
`VectorLayerService`. `DocumentVersion.SvgAssetId` queda sin usar por ahora (ningún consumidor
lo necesita todavía — ver "Fuera de alcance").

Geometría de cada `<path>` individual: **nunca se normaliza en tablas** (el propio criterio de
la tarjeta lo pide explícitamente — "evitar una tabla por cada punto Bézier"). El SVG sigue
siendo el formato canónico, ahora persistido como `Asset` real en vez de storage key efímero.

### IDs estables de Layer

El `groupId` que ya usa el flujo clásico (estable mientras no se regenere la paleta) se
**reutiliza verbatim como `Layer.Id`** en cada `DocumentVersion` nueva, en vez de generar un
Guid nuevo por versión. Así "modificar Layer → Save → reabrir" conserva la misma identidad de
layer que el usuario ya conoce desde antes de guardar, consistente con el criterio de
aceptación ("Layers con IDs estables"). Si un layer se agrega/quita entre saves, se
agregan/dejan de copiar sus filas — no hay remapeo.

`PaletteColor.Id` se genera nuevo en cada versión (no hay ningún consumidor, frontend o
backend, que dependa de su identidad entre versiones — el frontend nunca ve un
"paletteColorId" propio, solo `colorHex` por layer).

### Dimensiones físicas (WidthMm/HeightMm/ViewBox)

Ya existen en el flujo clásico vía el módulo `Dimensioning` (M1-S09,
`DimensionResponse`/`DimensionVersion`, endpoint `POST .../dimensions/apply`), **pero son
opcionales** — el usuario puede no haber aplicado dimensiones físicas nunca. El Workspace no
trackea hoy cuál fue la última dimensión aplicada (ningún estado en `useVectorDocument.ts`).

**Decisión**: el request de Save acepta `dimensionId: Guid?` opcional (el cliente lo manda si
el usuario llegó a aplicar dimensiones en esta sesión; `EditorHeader`/Workspace necesitan
trackear el último `DimensionResponse.DimensionId` recibido — cambio menor de frontend). Si se
manda, el Save resuelve esa `DimensionVersion` (mismo `IDimensionService.FindDimension`) y usa
su `WidthMm`/`HeightMm`. Si no se manda (nunca se aplicaron dimensiones), **default
1px = 1mm** (`WidthMm = SourceWidthPx`, `HeightMm = SourceHeightPx`) — mismo criterio que ya
usa `DimensionResponse` para mostrar la escala, nunca se infiere de DPI/EXIF.
`ViewBox = "0 0 {SourceWidthPx} {SourceHeightPx}"` siempre (no hay otra fuente de viewBox hoy).

## Diseño backend

Mismo patrón arquitectónico de M2.2-S03/S04: `Endpoint → XService → IXRepository → EF Core`,
result type discriminado, ownership siempre vía `IUserContext` + `IProjectRepository`.

### Nuevos endpoints (`/api/v2/projects/...`)

- `POST /api/v2/workspaces/save` — body:
  `{ projectId: Guid?, name: string?, classicProjectId: Guid, imageId: Guid, paletteId: Guid, paletteVersion: int, dimensionId: Guid? }`.
  - `projectId == null` → crea `Project` nuevo (`name` requerido en ese caso, mismas reglas de
    `ProjectService.ValidateName`), `201 Created`.
  - `projectId != null` → valida ownership (404 si no es del usuario o no existe, igual que
    `ProjectService.GetAsync`), agrega una `DocumentVersion` nueva al `VectorDocument`
    existente, `200 OK`. Optimistic concurrency vía `xmin` en `Project` igual que
    `ProjectService.UpdateAsync` → `409 Conflict` (`ProjectResult`-style) si hubo un Save
    concurrente.
  - Internamente: resuelve el layer set + paleta + layout + operaciones vigentes del triple
    clásico (reusando `IVectorLayerService`/`IColorPaletteService`/`ILayerLayoutService`/
    `IManufacturingOperationService`, igual que hace `ConsolidatedVectorLayerEndpoints` hoy —
    nunca confía en geometría/metadata mandada por el cliente), sube cada SVG de layer como
    `Asset` (`IAssetService`, ver abajo la extensión necesaria), y escribe
    `VectorDocument`/`DocumentVersion`/`Layer`/`PaletteColor` en una sola transacción EF Core.
  - Responde `VectorDocumentSaveResponse { projectId, versionNumber, savedAt }`.
- `GET /api/v2/projects/{projectId}/document` — devuelve la `DocumentVersion` actual
  (`Project.CurrentVersionId`) completa: dimensiones, viewBox, layers (con URL de descarga de
  su `Asset` cada uno), paleta. 404 si no es del usuario (mismo criterio de no-revelar
  existencia). Es el endpoint que la reapertura usa para reconstruir el `VectorDocument` sin
  pasar por el flujo clásico.
- Mutaciones post-Save (order/visible/locked/name/operación) sobre un `Project.Id` v2 ya
  existente: **se agregan a esta tarjeta** como sub-recursos simples sobre `Layer`
  (`PATCH /api/v2/projects/{projectId}/layers/{layerId}`, body parcial igual semántica PATCH
  que `ProjectService.UpdateAsync` — `null` = sin cambios), para que el Workspace guardado deje
  de depender del todo de los sidecars tras el primer Save, consistente con la decisión de
  cutover. Reordenar (`reorderLayers`) es un PATCH de `Order` por layer, sin endpoint de batch
  nuevo (la tarjeta no lo pide explícitamente y el patrón PATCH individual ya cubre el caso de
  uso del drag-and-drop, que reordena de a un layer por vez contra el backend clásico también).

### Extensión mínima a `IAssetService`

`UploadAsync` toma `IFormFile`, pensado para multipart HTTP. El Save necesita subir bytes
generados server-side (el SVG ya filtrado/coloreado que produce `VectorLayerService`). Se
agrega:
```csharp
Task<AssetResult> CreateFromBytesAsync(
    Guid projectId, string type, string fileName, string contentType, byte[] content,
    CancellationToken cancellationToken);
```
Reusa exactamente la misma validación/`AssetKeyFactory`/política storage-primero-fila-después
que `UploadAsync` — nunca expuesto como endpoint público, solo uso interno desde
`VectorDocumentService`.

### Nueva entidad de aplicación: `VectorDocumentService`/`IVectorDocumentRepository`

Mismo patrón exacto que `ProjectService`/`IProjectRepository`. `VectorDocumentResult`
discriminado: `Ready`, `NotFound`, `ValidationFailed`, `Conflict` (concurrencia),
`UpstreamError` (fallo leyendo el estado clásico — layer set no existe, paleta no confirmada,
etc., mapeado a 422 igual criterio que endpoints de vectorización existentes).

### Migración EF Core

- `AddLayerSvgAsset`: `Layer.SvgAssetId` (`Guid?`, FK a `Asset`, `OnDelete: SetNull` — borrar
  el asset no debe cascadear el borrado del layer, es metadata recuperable con un nuevo Save).

## Diseño frontend

### Estado Dirty/Saving/Saved/Error (greenfield confirmado)

`EditorHeader.tsx` hoy es un placeholder estático ("Sin guardado automático todavía", botón
Guardar deshabilitado). Se reemplaza por una máquina de estados real:
`'idle' | 'dirty' | 'saving' | 'saved' | 'error'`, dueña del nuevo hook `useWorkspaceSave`
(nuevo archivo `frontend/src/hooks/useWorkspaceSave.ts`):
- `idle→dirty`: cualquier mutación del `VectorDocument` (rename/toggle/reorder/operación)
  marca dirty. Guardado NO automático (fuera de alcance — ver "Fuera de alcance"): el usuario
  dispara el Save con el botón.
- `dirty→saving→saved`: al click en Guardar, `POST /api/v2/workspaces/save`. `saved` solo tras
  confirmación 200/201 del backend — nunca optimista (requisito explícito de la tarjeta:
  "No mostrar Saved antes de confirmación del backend").
- `saving→error`: fallo de red/409/422/500 → mensaje de error visible, reintentable, el
  documento vuelve a quedar `dirty` (no se pierde el intento de guardar).
- Al terminar un Save exitoso con `projectId` nuevo (primer Save), el Workspace guarda ese
  `Project.Id` en su estado y lo agrega a la URL (ver Reapertura) sin recargar la página.

### Reapertura

`workspaceLocation.ts` gana un cuarto campo opcional `savedProjectId` en el
`URLSearchParams`. Si está presente, `App.tsx` lo prioriza: en vez de reconstruir el
Workspace desde `GET /api/v1/projects/{projectId}/images/{imageId}` (flujo clásico), llama a
`GET /api/v2/projects/{projectId}/document` y reconstruye `VectorDocument` directo desde esa
respuesta (sin pasar por `useVectorDocument`'s agregación de 3 endpoints clásicos). Si
`savedProjectId` está ausente, el comportamiento existente de M2.1-S08 no cambia (deep-link
clásico, documento en staging).

## Fuera de alcance (según spec de la tarjeta + decisiones de esta sesión)

- Login real/OAuth (`M2.2-S09`), editor avanzado, IA.
- Autosave/debounce — el Save es siempre una acción explícita del usuario en esta tarjeta.
- Migración retroactiva de proyectos clásicos ya vectorizados-pero-nunca-guardados a `Project`
  v2.
- Migración completa de los sidecars `LayerLayout`/`ManufacturingOperation` a `Data.Layer` —
  solo el cutover al momento del Save (decisión arquitectónica #2).
- Object Storage real (S3/MinIO) — `IFileStorage` sigue siendo únicamente `LocalFileStorage`,
  mismo diferimiento ya documentado en M2.2-S04.
- `DocumentVersion.SvgAssetId` (snapshot combinado) — no se genera ni se usa en esta tarjeta,
  solo `Layer.SvgAssetId` por layer.
- Endpoint de batch-reorder — `PATCH` individual por layer alcanza para el caso de uso actual.
- "Mis proyectos"/listado — ya existe desde M2.2-S03, sin cambios acá.

## Tests (pedidos explícitamente por la tarjeta)

- Round-trip: Save → reabrir vía `GET .../document` → documento equivalente (mismos layers,
  IDs, paleta, dimensiones, operaciones).
- Documento multicolor (≥2 layers con distinto color/operación).
- Reload de la misma sesión sin reiniciar nada.
- Reinicio real de containers (`docker compose down && up`) entre Save y reapertura —
  verificación manual, además de los tests automatizados con Testcontainers.
- Fallo de DB durante Save (simulado) → `error` state en frontend, sin filas a medias
  (transacción EF Core).
- Fallo de Storage durante Save (simulado, p. ej. `IFileStorage` lanzando) → mismo criterio,
  sin `Layer` apuntando a un `Asset` inexistente.
- Documento incompleto (paleta no confirmada, layer set inexistente) → `422`/`UpstreamError`,
  nunca una fila a medio escribir.
- Versión de schema no soportada — `VectorDocument.SchemaVersion` ya existe desde M2.2-S02;
  esta tarjeta fija su valor inicial (`1`) y el endpoint de reapertura rechaza explícitamente
  (`422`) cualquier `SchemaVersion` mayor al que el backend entiende, en vez de intentar leerla
  igual.

## Definition of Done

El documento deja de depender de memoria/local state: un `Project` guardado sobrevive a un
reinicio completo de la aplicación (frontend recargado desde cero + containers reiniciados) y
se reabre de forma fiable y equivalente vía `GET /api/v2/projects/{projectId}/document`.
