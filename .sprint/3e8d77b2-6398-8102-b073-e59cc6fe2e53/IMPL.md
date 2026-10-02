# IMPL.md — M2.2-S05 · Persistencia completa del VectorDocument

## Resumen

Primera tarjeta que escribe filas originales en `VectorDocument`/`DocumentVersion`/`Layer`/
`PaletteColor` (M2.2-S02) — hasta acá solo `ProjectRepository.DuplicateAsync` las copiaba.
Implementa el puente Project clásico → v2 (auto-creado en el primer Save) y el cutover de los
sidecars `LayerLayout`/`ManufacturingOperation` en el límite del Save, ambos confirmados con el
usuario antes de escribir el spec.

## Backend

- `Data/Layer.cs`/`VectorizationDbContext.cs` — `Layer.SvgAssetId` (FK a `Asset`, `SetNull`).
- Migración `20261002155431_AddLayerSvgAsset` (aditiva).
- `Assets/IAssetService.cs`/`AssetService.cs` — `CreateFromBytesAsync` nuevo: sube SVGs
  generados server-side sin pasar por `IFormFile`, reusa `AssetKeyFactory`/política
  storage-primero-fila-después tal cual.
- `VectorDocuments/{VectorDocumentResult,DocumentSnapshot,IVectorDocumentService,VectorDocumentService}.cs`
  — orquesta: resuelve paleta/layer set/layout/operaciones/dimensión vigentes del triple
  clásico (nunca confía en lo que mande el cliente), sube cada SVG de capa como `Asset`, arma
  el snapshot y delega la escritura transaccional.
- `VectorDocuments/Persistence/{IVectorDocumentRepository,VectorDocumentRepository}.cs` —
  escribe `VectorDocument`/`DocumentVersion`/`Layer`/`PaletteColor` + repunta
  `Project.CurrentVersionId`, con upsert-por-Id para `Layer`, en una transacción EF explícita
  de dos fases (mismo patrón de `ProjectRepository.DuplicateAsync`).
- `Contracts/VectorDocumentRequests.cs`/`VectorDocumentResponse.cs`,
  `Endpoints/VectorDocumentEndpoints.cs` — `POST /api/v2/workspaces/save`,
  `GET /api/v2/projects/{id}/document`, `PATCH /api/v2/projects/{id}/layers/{layerId}`.
- `Program.cs` — registro DI + mapeo de endpoints.

## Frontend

- `hooks/useWorkspaceSave.ts` (+ test) — máquina de estados `idle/dirty/saving/saved/error`;
  `saved` solo tras 200/201 real del backend, nunca optimista.
- `components/editor/EditorHeader.tsx` (+ test) — reemplaza el placeholder estático
  ("Sin guardado automático todavía") por el estado real.
- `components/editor/EditorShell.tsx` — marca dirty en cada mutación (toggle/rename/
  reorder/operación), dispara `save()`, notifica `onSaved` a `App.tsx`.
- `lib/workspaceLocation.ts` (+ test) — campo opcional `savedProjectId`.
- `hooks/useVectorDocument.ts` (+ test) — si `savedProjectId` está presente, reconstruye el
  documento vía `GET .../document` sin pasar por la agregación clásica de 3 endpoints.
- `api/vectorDocumentApi.ts`, `types/vectorDocument.ts`, `api/httpClient.ts` (`patchJson` nuevo).
- `App.tsx` — deep-link con `savedProjectId` prioriza `GET .../document`; trackea
  `workspacePaletteId`/`savedProjectId` por separado de `confirmedPalette` (la reapertura
  guardada no llama a `GET .../color-palette/{paletteId}`).

## Decisiones técnicas (no cubiertas en detalle de código por spec.md)

1. **Upsert-por-Id de `Layer`**: `Layer.Id` es PK global de la tabla (una sola fila en TODA la
   base, nunca una por versión) — reutilizar el `groupId` verbatim en un segundo Save real
   violaría esa PK si se insertara de nuevo. Se resuelve con upsert: si el `groupId` ya existía
   en una versión anterior del mismo `VectorDocument`, esa fila se reutiliza in-place y se
   repunta a la versión nueva, en vez de insertar una segunda. Efecto secundario aceptado: una
   versión superada pierde las filas de los layers que "migraron" a la siguiente — no rompe
   nada porque `GET .../document` solo lee la versión ACTUAL (`Project.CurrentVersionId`),
   nunca el historial completo. Mismo criterio deja `PaletteColor` de versiones superadas
   huérfano (sin ningún `Layer.ColorId` apuntándole) — aceptado, ningún consumidor lee paletas
   de versiones viejas.
2. **Transacción EF explícita de dos fases**: el ciclo real `Project ↔ VectorDocument ↔
   DocumentVersion` (`Project.CurrentVersionId` → `DocumentVersion` nueva) obliga a partir la
   escritura en dos `SaveChangesAsync` (mismo patrón que `ProjectRepository.DuplicateAsync`):
   fase 1 inserta `VectorDocument`/`DocumentVersion`/`Layer`/`PaletteColor` con
   `Project.CurrentVersionId` todavía intacto (sin ciclo), fase 2 repunta `Project` a la
   versión recién persistida (acá se evalúa `xmin`, `DbUpdateConcurrencyException` → 409). Una
   transacción EXPLÍCITA envuelve ambas fases porque un fallo entre medio no debe dejar la
   primera ya comprometida.
3. **`_dbContext.Add(...)` explícito** sobre `VectorDocument`/`DocumentVersion`/`Layer`/
   `PaletteColor` nuevos: al llegar al change tracker vía fixup de la colección de navegación
   de un `Project` YA EXISTENTE (`Unchanged`), EF Core asume — por tener ya un `Id` de Guid no
   default asignado por la aplicación — que la fila YA EXISTE y genera un `UPDATE` en vez de un
   `INSERT` (afecta 0 filas, dispara `DbUpdateConcurrencyException` falso). El `Add` explícito
   evita este gotcha.
4. **Orden de validación en `SaveAsync`**: paleta-confirmada antes que layer-set-existe (ambos
   técnicamente ciertos cuando aplica; el primero da un código de error más específico).
5. **`UpstreamError` siempre → 422** (incluye fallos de storage subiendo el SVG de una capa),
   consistente con spec.md.
6. **Frontend**: el triple clásico sigue siendo obligatorio en la URL incluso con
   `savedProjectId` presente (se agrega, no reemplaza) porque las mutaciones (toggle/rename/
   reorder/operación) siguen resolviéndose contra los sidecars clásicos en esta tarjeta — el
   cutover de esas llamadas a los `PATCH` v2 nuevos no fue parte del alcance frontend de esta
   tarjeta (el backend ya expone `PATCH /api/v2/.../layers/{layerId}`, listo para una futura
   tarjeta que haga ese cutover del lado del cliente).

## Bug real encontrado y corregido en la revisión del orquestador

**`pathCount` quedaba hardcodeado en `0` para cualquier documento reabierto.**
`fromSavedDocument` (frontend) no tenía de dónde sacar ese número porque `Data.Layer` (M2.2-S02)
nunca lo guardaba — la geometría vive solo como SVG (`Layer.SvgAssetId`), y `pathCount` es un
conteo que hasta ahora SOLO existía en memoria (`VectorVersion.Metrics.PathCount`, resuelto por
`ConsolidatedVectorLayerEndpoints` en el flujo clásico). Esto no era cosmético: `InspectorPanel`/
`LayerInfoPanel` muestran `pathCount` directamente (`<dd>{consolidated.pathCount}</dd>`, sin
fallback a "—" cuando el valor es un número real pero falso), y `useVectorDocument.selectAllInLayer`
itera `0..pathCount-1` para seleccionar todos los paths de una capa — con `pathCount: 0` ese loop
no selecciona nada, rompiendo "Seleccionar todo en la capa" en silencio para cualquier proyecto
reabierto.

Corregido con una migración aditiva mínima (`Layer.PathCount`, migración
`20261002170728_AddLayerPathCount`): `VectorDocumentService.SaveAsync` ya resuelve
`vectorVersion` (el mismo objeto que usa para leer el SVG a subir) — se persiste
`vectorVersion.Metrics.PathCount` junto con el resto del snapshot, sobrevive el upsert-por-Id
igual que los demás campos, se expone en `VectorDocumentLayerResponse.pathCount`, y el frontend
ahora lo usa real en vez de `0`. `componentCount` se deja en `null` sin cambios — ese valor
siempre fue opcional incluso en el flujo clásico en vivo (el panel ya muestra "Sin calcular"
cuando es `null`), así que no había ningún dato falso ahí.

Tests agregados/actualizados: `VectorDocumentRepositoryTests` (asserts de `PathCount` en el Save
inicial y tras el upsert-por-Id del segundo Save), `VectorDocumentEndpointsTests` (assert
`PathCount > 0` en el round-trip), `useVectorDocument.test.ts` (fixture + assert de
`pathCount: 5` reconstruido desde `GET .../document`).

## Verificación (5 comandos, confirmados de forma independiente por el orquestador, DESPUÉS del fix de PathCount)

1. `dotnet build` → 0 errores, 0 advertencias.
2. `dotnet test` → **742/742** (incluye las aserciones de `PathCount` agregadas en la revisión).
3. `pytest` (python-engine) → 400/400, sin tocar.
4. `npm test` → 324/325 en una corrida (1 falla aislada en `ManufacturingOperations.test.tsx`,
   no tocado por esta tarjeta, confirmado flake por contención de CPU: pasa 5/5 en aislamiento
   corrido aparte), 325/325 en la corrida previa al fix de PathCount.
5. `npm run build` → limpio, 0 errores de TypeScript.

Revisión de código personal completa: `VectorDocumentService.cs`, `VectorDocumentRepository.cs`,
`VectorDocumentEndpoints.cs`, `AssetService.CreateFromBytesAsync`, `useWorkspaceSave.ts`,
`EditorShell.tsx`, `useVectorDocument.ts`, `App.tsx`, `workspaceLocation.ts` — diseño
consistente con el patrón `Endpoint → Service → Repository` ya establecido, decisiones
arquitectónicas del spec implementadas tal cual se confirmaron con el usuario, un bug real
encontrado y corregido antes de mergear.

Spec congelado: `.sprint/3e8d77b2-6398-8102-b073-e59cc6fe2e53/spec.md`
