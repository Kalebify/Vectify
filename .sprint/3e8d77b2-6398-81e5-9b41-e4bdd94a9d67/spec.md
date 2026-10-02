# spec.md — M2.2-S06 · Versionado persistente de documentos

## Contexto

Sexta tarjeta de MVP 2.2. M2.2-S05 ya hace que cada Save cree una `DocumentVersion` nueva
(numerada, `VersionNumber` único por `VectorDocumentId`) y repunte `Project.CurrentVersionId`.
Esta tarjeta expone esa historia como una API real (listar, ver metadata de una versión
cualquiera, restaurar) y, al escribir el spec, se detectaron **dos conflictos reales entre lo
que M2.2-S05 construyó y lo que esta tarjeta exige explícitamente** ("cada checkpoint crea
versión numerada INMUTABLE", "restaurar V3 cuando current=V8 no elimina V4–V8"). Ambos se
resuelven en esta tarjeta porque no implementarlos dejaría el feature de versionado construido
sobre una base que no es realmente inmutable — no son decisiones de producto con alternativas
razonables, son correcciones técnicas directas al requisito ya escrito.

## ⚠️ Conflictos reales detectados con M2.2-S05 (resueltos en esta tarjeta)

### 1. El upsert-por-Id de `Layer` viola la inmutabilidad

`VectorDocumentRepository.SaveAsync` (M2.2-S05) reutiliza la fila de `Layer` de la versión
anterior cuando el mismo `groupId` vuelve a guardarse (actualiza sus campos in-place y la
repunta a la versión nueva), documentado en su momento como aceptable porque "`GET .../document`
solo lee la versión ACTUAL". Pero esta tarjeta pide exactamente lo contrario: `GET
.../versions/{n}` debe poder leer **cualquier** versión histórica completa, y "restaurar V3"
exige que V3 siga teniendo sus propios `Layer`/`PaletteColor` intactos aunque hayan pasado V4,
V5... V8 desde entonces. Con el upsert actual, cualquier layer que no cambió entre saves queda
"robado" por la versión más nueva — V3 quedaría con huecos.

**Fix**: cada checkpoint (Save) vuelve a copiar TODOS los layers vigentes como filas 100% nuevas
(nuevo `Guid` por fila, mismo criterio que `ProjectRepository.DuplicateAsync`), nunca reutiliza
una fila de una versión anterior. `Layer.Id` dejar de ser el `groupId` clásico reutilizado
verbatim (eso violaba la PK y forzaba el upsert) — se agrega `Layer.GroupId` (`Guid`, columna
normal, NO PK, índice único compuesto `(VersionId, GroupId)`) que preserva la correlación "esta
fila representa el mismo layer conceptual que esa otra fila de otra versión" para quien la
necesite (hoy: el frontend, al reconstruir `VectorDocumentLayer.groupId` desde `GET
.../document`), mientras `Layer.Id` vuelve a ser una PK realmente inmutable por fila. Efecto:
el repositorio se SIMPLIFICA (ya no hace falta la rama upsert-vs-insert de M2.2-S05, siempre es
insert) a cambio de una migración aditiva (`Layer.GroupId`) + una de borrado de la columna vieja
reutilizada como PK (no aplica — `Layer.Id` sigue siendo `Guid`, solo cambia de dónde sale su
valor: `Guid.NewGuid()` en vez de `layerSnapshot.LayerId`).

### 2. `WidthMm`/`HeightMm`/`ViewBox`/`SchemaVersion` viven en `VectorDocument`, no en `DocumentVersion`

Estos 4 campos están en la entidad padre `VectorDocument` (M2.2-S02) y
`VectorDocumentRepository.SaveAsync` los sobrescribe sin versionar en cada Save. Restaurar V3
hoy NO podría devolver las dimensiones que tenía V3 si cambiaron en saves posteriores — rompe
la misma garantía de inmutabilidad/restauración que el punto 1.

**Fix**: migración que mueve estas 4 columnas de `vector_documents` a `document_versions`
(`WidthMm`, `HeightMm`, `ViewBox`, `SchemaVersion` pasan a ser propiedades de
`DocumentVersion`). `VectorDocument` queda como una entidad delgada (`Id`, `ProjectId`,
`Versions`) — identidad/agrupación pura, sin estado propio. Sin datos reales en producción
todavía (nada mergeado usa esto fuera de este repo de desarrollo), así que la migración no
necesita backfill: es un `DropColumn` en `vector_documents` + `AddColumn` en `document_versions`.

## Modelo (sin cambios de alto nivel, ya existe desde M2.2-S02/S05)

`Project → VectorDocument → DocumentVersion 1..N`. `Project.CurrentVersionId` apunta a la
versión vigente (ya existe, ya usa concurrencia optimista vía `xmin`, sin cambios).

## Origin

`DocumentVersion.Origin` ya existe como columna de texto libre (M2.2-S02). Esta tarjeta fija su
vocabulario cerrado a nivel de aplicación (no un enum de Postgres, mismo criterio que
`ManufacturingOperationKind` — validado/convertido en C#): `VECTORIZE`, `MANUAL_EDIT`,
`AUTOSAVE`, `RESTORE`, `AI_EDIT`. De estos, hoy solo **dos se producen realmente**:
- `MANUAL_EDIT`: lo que M2.2-S05 mandaba como `"workspace_save"` — se renombra al valor
  canónico de este vocabulario (único cambio de comportamiento sobre S05: el string guardado).
- `RESTORE`: nuevo en esta tarjeta, lo pone el endpoint de restaurar.

`VECTORIZE` y `AUTOSAVE` quedan reservados (ningún endpoint los emite todavía — no hay
autosave ni un flujo de "primera versión automática desde vectorización clásica" en ningún
card actual) y `AI_EDIT` reservado para IA futura, igual que dice la tarjeta.

## Regla de restauración

Restaurar V3 cuando `current = V8` **nunca borra ni modifica V4–V8**: crea una `DocumentVersion`
nueva (V9, siguiente número secuencial) con una copia fresca (ids nuevos) de los `Layer`/
`PaletteColor`/dimensiones/viewBox/schemaVersion de V3, `Origin: RESTORE`,
`MetadataJson: {"restoredFromVersion": 3}`, y repunta `Project.CurrentVersionId` a V9. Los
`SvgAssetId` de los layers restaurados se reusan tal cual (el Asset ya existe en storage, no
hace falta volver a subir nada — más rápido que un Save normal, que sí resuelve el estado
clásico vigente).

## Concurrencia

Ya resuelto por M2.2-S05 (`Project.xmin`, `DbUpdateConcurrencyException` → 409). El endpoint de
restaurar usa el mismo mecanismo: dos restores/saves concurrentes sobre el mismo `Project`, el
segundo en commitear pierde con 409.

## API nueva

- `GET /api/v2/projects/{projectId}/versions` — lista TODAS las versiones del documento
  (`versionNumber`, `origin`, `createdAt`, `widthMm`/`heightMm`/`viewBox`/`schemaVersion`,
  **sin** el array de layers — eso es carga completa, ver el siguiente endpoint), orden
  `versionNumber` descendente (la más reciente primero). 404 si el proyecto no existe/no es del
  usuario efectivo/no tiene ningún documento guardado todavía (mismo criterio uniforme que
  `GET .../document`). Sin paginación (alcance de esta tarjeta: historiales de un MVP no
  justifican esa complejidad todavía).
- `GET /api/v2/projects/{projectId}/versions/{versionNumber}` — la versión completa (mismo
  shape que devuelve hoy `GET .../document`, reusa `VectorDocumentResponse`/`ToDocumentResponse`
  tal cual), para CUALQUIER `versionNumber` válido del documento, no solo la actual. 404 si el
  número de versión no existe para ese documento.
- `POST /api/v2/projects/{projectId}/versions/{versionNumber}/restore` — crea la versión nueva
  descrita arriba. Responde igual que `POST .../workspaces/save` (`VectorDocumentSaveResponse`:
  `projectId`, nuevo `versionNumber`, `savedAt`). 404 si la versión origen no existe; 409 si hay
  conflicto de concurrencia.
- `GET /api/v2/projects/{projectId}/document` (M2.2-S05, sin cambios de contrato) sigue siendo
  el atajo para "la versión actual" — equivalente a resolver `Project.CurrentVersionId` y
  llamar al endpoint de arriba con ese número, no se duplica lógica entre ambos (el segundo
  reusa el mismo método de servicio que el primero, parametrizado por "actual" vs "número
  explícito").
- `POST /api/v2/workspaces/save` (M2.2-S05): sin cambios de contrato, solo el string interno de
  `Origin` pasa de `"workspace_save"` a `"MANUAL_EDIT"`. Es, en el vocabulario de esta tarjeta,
  el "crear checkpoint" que pide el criterio de aceptación — no se crea un endpoint nuevo
  separado para eso.

## Backend: diseño

Mismo patrón arquitectónico de M2.2-S03/S04/S05: `Endpoint → IVectorDocumentService →
IVectorDocumentRepository → EF Core`. Se extienden las interfaces ya existentes (no se crea un
servicio/repositorio paralelo):
- `IVectorDocumentRepository.SaveAsync` deja de hacer upsert-por-Id (ver conflicto #1):
  siempre inserta `Layer`/`PaletteColor` nuevos, trasladando `WidthMm`/`HeightMm`/`ViewBox`/
  `SchemaVersion` al nuevo `DocumentVersion` en vez de mutar `VectorDocument`.
- `IVectorDocumentRepository.ListVersionsAsync(projectId, ownerId)` — nuevo.
- `IVectorDocumentRepository.FindVersionAsync(projectId, ownerId, versionNumber)` — nuevo,
  generaliza el `FindCurrentDocumentAsync` existente (que pasa a ser un caso particular:
  resolver `Project.CurrentVersionId` primero y delegar a este método con ese número).
- `IVectorDocumentRepository.RestoreAsync(projectId, ownerId, versionNumber, cancellationToken)`
  — nuevo: misma transacción EF explícita de dos fases que `SaveAsync` (ver M2.2-S05 IMPL.md
  para el razonamiento completo del ciclo `Project ↔ VectorDocument ↔ DocumentVersion`).
- `VectorDocumentResult` gana `VersionListReady`/`NotFound` (ya existe) para los casos nuevos.

## Fuera de alcance

- Diff/comparación entre versiones (contenido de qué cambió de V_n a V_n+1).
- Poda/retención de historial (borrar versiones viejas) — "sin borrar historia" es requisito
  explícito, lo contrario ni se contempla.
- Paginación del listado de versiones.
- `AUTOSAVE`/`VECTORIZE`/`AI_EDIT` reales — solo el vocabulario queda definido, ningún flujo los
  produce todavía.
- UI de historial de versiones en el frontend — esta tarjeta es backend puro (la tarjeta no
  menciona componentes de UI, a diferencia de M2.2-S05 que sí pedía el botón Guardar real).

## Tests (pedidos explícitamente por la tarjeta)

- Secuencia: 3+ saves consecutivos → V1/V2/V3 todas inmutables (un layer sin cambios entre
  saves sigue existiendo, con sus propios ids, en CADA versión donde estuvo presente — el test
  que prueba exactamente el bug corregido del conflicto #1).
- Restore: current=V8, restaurar V3 → crea V9 (no V4..V8 modificadas/eliminadas), V9 tiene el
  mismo contenido que V3 (dimensiones, layers, paleta), `Project.CurrentVersionId == V9.Id`.
- Concurrencia: dos restores/saves concurrentes sobre el mismo proyecto → uno 200/201, el otro
  409.
- Versión inexistente: `GET`/`restore` de un `versionNumber` que no existe para ese documento →
  404.
- Documento eliminado: `Project` soft-eliminado (M2.2-S03) → todos los endpoints de versión
  404 (ya se excluye solo por el query filter global de `Project`, confirmar con un test).

## Definition of Done

La historia completa del documento es auditable (se puede listar y ver cualquier versión
pasada tal cual quedó, sin huecos) y recuperable (restaurar una versión vieja nunca destruye
versiones intermedias).
