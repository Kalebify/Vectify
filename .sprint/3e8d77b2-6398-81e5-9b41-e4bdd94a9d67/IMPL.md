# IMPL.md — M2.2-S06 · Versionado persistente de documentos

## Resumen

Sexta tarjeta de MVP 2.2. Expone la historia de `DocumentVersion` como API real (listar, ver
cualquier versión, restaurar) y corrige los dos conflictos reales de inmutabilidad detectados
contra M2.2-S05 al escribir el spec (ver spec.md, "⚠️ Conflictos reales detectados").

## Fix conflicto #1: inmutabilidad real de `Layer`

`Layer.GroupId` (Guid, columna normal, NO la PK, índice único compuesto `(VersionId, GroupId)`)
reemplaza el uso de `Layer.Id` como el `groupId` clásico reutilizado verbatim. `Layer.Id` vuelve
a ser una PK realmente inmutable: cada checkpoint (`SaveAsync`/`RestoreAsync`) inserta filas
`Layer`/`PaletteColor` 100% nuevas (`Guid.NewGuid()` propio), nunca reutiliza/actualiza una fila
de una versión anterior. `UpdateLayerAsync`/`ToLayerResponse` se adaptaron para resolver/exponer
por `GroupId` (scopeado a `Project.CurrentVersionId`), no por `Layer.Id` — sin esto, el PATCH
habría quedado roto en cuanto hubiera más de un Save, porque `Layer.Id` deja de ser estable.

**Efecto colateral intencional**: el campo `"id"` del contrato `VectorDocumentLayerResponse` (y
de la ruta `PATCH .../layers/{layerId}`) ahora representa `Layer.GroupId`, no `Layer.Id` — sin
cambio de nombre en el contrato, así que el frontend (que ya trataba ese valor como `groupId`
desde M2.2-S05) sigue funcionando sin cambios.

## Fix conflicto #2: dimensiones versionadas

`WidthMm`/`HeightMm`/`ViewBox`/`SchemaVersion` se movieron de `VectorDocument` (padre, sin
versionar) a `DocumentVersion`. `VectorDocument` quedó como entidad delgada (`Id`, `ProjectId`,
`Versions`). Una sola migración (`MoveDocumentDimensionsToVersion`) cubre ambos conflictos:
`DropColumn` de las 4 columnas en `vector_documents`, `AddColumn` de las mismas 4 en
`document_versions`, `AddColumn GroupId` en `layers`, `CreateIndex` único en
`(VersionId, GroupId)`. Sin backfill — no hay datos reales en producción todavía.

## Vocabulario cerrado de `Origin`

Nuevo enum `DocumentVersionOrigin` (`Vectorize`/`ManualEdit`/`Autosave`/`Restore`/`AiEdit`), con
conversión EF explícita (`DocumentVersionOriginParser`, mismo patrón que
`ManufacturingOperationKind`). `VectorDocumentService.SaveAsync` emite `ManualEdit`
(persistido `"MANUAL_EDIT"`) en vez del string libre `"workspace_save"` de M2.2-S05. Solo
`ManualEdit` y `Restore` se producen realmente hoy; `Vectorize`/`Autosave`/`AiEdit` quedan
reservados sin ningún código que los emita. A diferencia de `ManufacturingOperationParser`,
`Origin` nunca viaja como input de un request HTTP (siempre lo decide el servidor), así que
`Parse` lanza ante un valor desconocido en vez de devolver `null` silenciosamente.

## Endpoints nuevos

- `GET /api/v2/projects/{projectId}/versions` — lista metadata de todas las versiones
  (`versionNumber`, `origin`, `createdAt`, dimensiones/viewBox/schemaVersion, sin layers),
  orden `versionNumber` descendente. Sin paginación.
- `GET /api/v2/projects/{projectId}/versions/{versionNumber}` — versión completa para
  cualquier número válido. Reusa el mismo `ToDocumentResponse`/método de servicio que
  `GET .../document` (que ahora es un atajo que resuelve `Project.CurrentVersionId` y delega),
  sin duplicar lógica entre ambos.
- `POST /api/v2/projects/{projectId}/versions/{versionNumber}/restore` — crea una versión
  nueva (siguiente número secuencial) con copia fresca (ids nuevos, mismos `SvgAssetId`
  reusados tal cual — no hace falta volver a subir nada a storage) del contenido de la versión
  origen, `Origin: RESTORE`, `MetadataJson: {"restoredFromVersion": N}`, repunta
  `Project.CurrentVersionId`. Misma transacción EF explícita de dos fases y mismo manejo de
  concurrencia (`xmin` → 409) que `SaveAsync`.

`POST /api/v2/workspaces/save` (M2.2-S05) sigue siendo el "crear checkpoint" de esta tarjeta —
no se creó un endpoint nuevo separado para eso, solo cambió el valor de `Origin` que emite.

## Decisiones técnicas adicionales (no cubiertas en detalle de código por spec.md)

- **`ProjectRepository.DuplicateAsync`** (M2.2-S03) también dependía de la forma vieja del
  modelo — no estaba en el alcance explícito de la tarjeta, pero no compilaba sin el fix: ahora
  copia `WidthMm`/`HeightMm`/`ViewBox`/`SchemaVersion` desde cada `DocumentVersion` origen (ya
  no desde `VectorDocument`), y copia `Layer.GroupId` tal cual (evita que todas las filas
  duplicadas colisionen en `Guid.Empty` contra el nuevo índice único compuesto).
- **`UpdateLayerAsync`** se simplificó: ya no necesita cargar `Version`/`VectorDocument` vía
  `Include` anidado — resuelve directo por `(VersionId == Project.CurrentVersionId, GroupId ==
  layerId)`.
- **PATCH sigue mutando in-place la versión actual** (no crea una versión nueva por cada
  edición de campo) — decisión consistente con que "checkpoint" en el vocabulario de esta
  tarjeta es un Save/Restore explícito, no cada mutación individual; la granularidad de
  versionado queda atada a esas dos acciones, no a cada toggle/rename.

## Tests (pedidos explícitamente por la tarjeta)

- **Secuencia** (`SaveAsync_ThreeConsecutiveSaves_...`): 3 saves consecutivos, un layer sin
  cambios conserva su PROPIA fila (PK distinta) en CADA versión donde estuvo presente — prueba
  directa del fix del conflicto #1 (3 filas distintas en la tabla `layers` para el mismo
  `GroupId`, nunca una sola reutilizada).
- **Restore** (`RestoreAsync_RestoringAnOldVersion_...`): 8 saves, restaurar V3 con current=V8
  crea V9 (siguiente número secuencial), V4-V8 quedan totalmente intactas, V9 tiene el mismo
  contenido que V3 (dimensiones, viewBox, layer) con una fila NUEVA (no reutilizada),
  `Project.CurrentVersionId` repunta a V9.
- **Concurrencia**: `SaveAsync_TwoConcurrentSaves_...` y
  `RestoreAsync_ConcurrentWithAnotherSave_...` — el segundo en commitear lanza
  `DbUpdateConcurrencyException` (409 en el endpoint).
- **Versión inexistente**: `FindVersionAsync_NonexistentVersionNumber_ReturnsNull`,
  `RestoreAsync_NonexistentVersionNumber_ReturnsNull`.
- **Documento eliminado**: `AllVersionEndpoints_SoftDeletedProject_BehaveAsNonexistent` — el
  query filter global de `Project` (M2.2-S03) ya excluye proyectos soft-eliminados, confirmado
  con un test explícito sobre los 4 endpoints de versión.

## Verificación (5 comandos, confirmados de forma independiente por el orquestador)

1. `dotnet build` → 0 errores, 0 advertencias.
2. `dotnet test` → 759/759 (Testcontainers PostgreSQL real, `UseInMemoryDatabase` no se usó).
3. `pytest` (python-engine) → 400/400, sin tocar (tarjeta puramente backend ASP.NET Core).
4. `npm test` (frontend) → 325/325, sin tocar.
5. `npm run build` (frontend) → limpio, sin cambios de contrato necesarios del lado del cliente.

Revisión de código personal completa: `Data/Layer.cs`, `VectorDocumentRepository.cs`
(`SaveAsync`/`RestoreAsync`/`UpdateLayerAsync`), `VectorDocumentEndpoints.cs`,
`DocumentVersionOrigin.cs`, la migración `MoveDocumentDimensionsToVersion`, y los tests de
secuencia/restore — diseño consistente, las dos correcciones de inmutabilidad implementadas
exactamente como las documentó el spec, sin bugs nuevos encontrados en esta revisión.
