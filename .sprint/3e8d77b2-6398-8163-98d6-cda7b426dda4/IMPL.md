# M2.2-S03 · Project Repository + API CRUD -- IMPL.md

## Decisión crítica: `/api/v2/projects`, no `/api/v1/projects`

Confirmado y ejecutado tal cual lo pedía el spec (sección "⚠️ Conflicto real detectado"):
`POST /api/v1/projects` ya existe en `backend/Vectorify.Api/Endpoints/ProjectEndpoints.cs`
(línea 25) y es el endpoint de upload multipart del flujo clásico (MVP1) -- crea un
`ProjectRecord` (par `ProjectId`+`ImageId` de un archivo subido) vía
`PersistentProjectRegistry`, sin `OwnerId`/`Name`/`Description`. Es un concepto de dominio
distinto que coincide en nombre por casualidad con el `Project` persistente de M2.2-S02.

La API CRUD nueva de esta tarjeta vive íntegramente bajo `/api/v2/projects`
(`POST/GET /api/v2/projects`, `GET/PATCH/DELETE /api/v2/projects/{id}`,
`POST /api/v2/projects/{id}/duplicate`), implementada en
`backend/Vectorify.Api/Endpoints/ProjectV2Endpoints.cs`. **No se tocó**
`ProjectEndpoints.cs`, `IProjectRegistry`, `ProjectRecord`, `PersistentProjectRegistry` ni
ningún archivo del namespace `Vectorify.Api.Projects` (flujo clásico) -- coexisten sin
cambios, confirmado con `git status`/`git diff` antes de reportar.

## Estructura de carpetas/namespaces (para no mezclar con el flujo clásico)

- `backend/Vectorify.Api/Projects/Persistence/` (namespace `Vectorify.Api.Projects.Persistence`,
  NUEVO y distinto de `Vectorify.Api.Projects`): `IProjectRepository`, `ProjectRepository`,
  `ProjectListQuery`, `ProjectSortBy`. Siguiendo la recomendación literal del spec
  ("Projects/Persistence/"), pero en un sub-namespace separado para que ningún nombre
  colisione ni se confunda con `IProjectRegistry`/`ProjectRecord`.
- `backend/Vectorify.Api/ProjectManagement/` (namespace `Vectorify.Api.ProjectManagement`):
  `IProjectService`, `ProjectService`, `ProjectResult` -- la capa de aplicación, deliberadamente
  en un namespace propio (no `Vectorify.Api.Projects`) para que "ProjectService" (nuevo) nunca
  se confunda con `ProjectUploadService` (clásico, mismo namespace que `IProjectRegistry`).
- `backend/Vectorify.Api/Users/` (namespace `Vectorify.Api.Users`): `IUserContext`,
  `DevelopmentUserContext`, `DevelopmentUserSeeder`.
- `backend/Vectorify.Api/Contracts/ProjectResponse.cs` (`ProjectResponse`,
  `ProjectSummaryResponse`, `ProjectListResponse`) y `ProjectRequests.cs`
  (`CreateProjectRequest`, `UpdateProjectRequest`) -- mismo patrón que el resto de
  `Contracts/` (`ApiErrorResponse`, `LayerLayoutSetResponse`, etc.), nunca se expone la
  entidad EF `Vectorify.Api.Data.Project` directamente.

Arquitectura real: `ProjectV2Endpoints` → `IProjectService` (`ProjectService`) →
`IProjectRepository` (`ProjectRepository`) → EF Core (`VectorizationDbContext`). Ningún
endpoint toca `VectorizationDbContext` directamente.

## `IUserContext` / `DevelopmentUserContext`

```csharp
public interface IUserContext { Guid GetEffectiveUserId(); }
```

`DevelopmentUserContext.GetEffectiveUserId()` devuelve siempre el mismo
`Guid` fijo (`00000000-0000-0000-0000-000000000001`, constante `DevelopmentUserId`), sin
leer headers/cookies/JWT -- el método es deliberadamente síncrono (sin acceso a DB), tal
como lo recomienda el spec.

**Sembrado elegido: al ARRANCAR la API, no lazily.** Como `GetEffectiveUserId()` es
síncrono, no puede sembrar el `User` en el primer uso sin violar esa firma mínima. En vez
de eso, `DevelopmentUserSeeder.EnsureSeededAsync(dbContext)` se invoca en `Program.cs`,
en el MISMO bloque donde ya se aplican las migraciones automáticas al arrancar (tolerante a
fallos, mismo criterio que el resto de ese bloque: si Postgres no está configurado/no
responde, la API sigue arrancando igual). Es idempotente (`AnyAsync` antes de insertar), así
que reinicios sucesivos del proceso contra la misma base no duplican el usuario. Esto
garantiza que la FK `Project.OwnerId -> Users.Id` (NOT NULL + `Restrict`) nunca falle al
crear el primer proyecto de una sesión de desarrollo nueva.

`M2.2-S09` reemplaza/extiende `DevelopmentUserContext` sin que `ProjectService`/
`IProjectRepository` necesiten cambiar: ambos dependen únicamente de `IUserContext`.

`IProjectRepository` recibe el `ownerId` ya resuelto como parámetro EXPLÍCITO en cada
método (en vez de depender de `IUserContext` directamente) -- decisión deliberada para que
los tests del repositorio puedan ejercitar cualquier combinación de owner sin necesitar un
`IUserContext` real/mockeado. `ProjectService` es el único punto que llama a
`IUserContext.GetEffectiveUserId()`.

## Concurrencia optimista: `xmin` de PostgreSQL

Se investigó la versión instalada (`Npgsql.EntityFrameworkCore.PostgreSQL` 9.0.4, vía
reflection sobre el DLL): **no existe** un método `UseXminAsConcurrencyToken()` en esa
versión del paquete (se verificó con un script de reflection, cero resultados). El
mecanismo real soportado es una **shadow property** vía convención (confirmado leyendo el
XML doc del paquete, `NpgsqlPostgresModelFinalizingConvention.ProcessRowVersionProperty`:
"Detects properties which are uint, OnAddOrUpdate and configured as concurrency tokens, and
maps these to the PostgreSQL internal 'xmin' column"):

```csharp
entity.Property<uint>("xmin")
    .HasColumnName("xmin")
    .IsRowVersion();
```

Aplicado únicamente a `Project` en `VectorizationDbContext.OnModelCreating` (no a las demás
entidades -- el spec solo pide concurrencia sobre `Project`, que es la única con mutación
concurrente real vía PATCH en esta tarjeta).

La migración generada por `dotnet ef migrations add AddProjectConcurrencyToken` incluía por
defecto un `AddColumn<uint>("xmin", ...)`/`DropColumn("xmin", ...)` -- se **editaron a mano**
para dejarlas vacías: `xmin` es una columna de SISTEMA que PostgreSQL agrega automáticamente
a TODA tabla (contador MVCC interno), un intento real de `ALTER TABLE ADD COLUMN "xmin"`
falla en runtime con `column name "xmin" conflicts with a system column name`. La migración
queda en el historial (`__EFMigrationsHistory`) solo para marcar el punto desde el que el
modelo mapea `xmin`, sin ejecutar ningún DDL real. Ver el comentario de clase en
`Migrations/20261001170124_AddProjectConcurrencyToken.cs`.

Mecanismo de prueba (`ProjectRepositoryTests.UpdateAsync_TwoConcurrentUpdates_...`): dos
`DbContext` independientes hacen un "warm up read" de la misma fila ANTES de que cualquiera
escriba (necesario por la identity resolution de EF Core: una query posterior sobre una
entidad ya trackeada no refresca sus valores dentro del mismo contexto). El primer
`UpdateAsync` guarda con éxito y avanza el `xmin` real en la base; el segundo
`UpdateAsync` -- todavía anclado al `xmin` viejo -- falla con
`DbUpdateConcurrencyException` real (no simulada). `ProjectService.UpdateAsync` la captura y
la traduce a `ProjectResult.Conflict` (409, código `concurrency_conflict`).

## Política de Assets al soft-deletear un Project

Implementada tal cual la recomendación del spec: `ProjectRepository.SoftDeleteAsync` SOLO
setea `DeletedAt`/`UpdatedAt` sobre la fila `Project` -- nunca toca la tabla `assets`. Los
Assets de un proyecto soft-deleteado siguen existiendo en la tabla, simplemente
inalcanzables a través de ese `Project` mientras el global query filter de M2.2-S02
(`DeletedAt == null`) lo excluya. Si el proyecto se restaura (sin endpoint de restore en
esta tarjeta, fuera de alcance), sus Assets quedarían intactos. Limpieza real de Assets
huérfanos: responsabilidad de `M2.2-S04` o una tarjeta de mantenimiento futura.

## Duplicate: Assets compartidos por referencia, no duplicados

`ProjectRepository.DuplicateAsync` duplica `VectorDocument`/`DocumentVersion`/`Layer`/
`PaletteColor` con Ids nuevos propios (nunca reutiliza ningún Id del original, verificado en
tests). Los `Asset` (binarios SVG/thumbnail) NO se duplican -- fuera de alcance explícito de
esta tarjeta (`M2.2-S04`, sin Object Storage real todavía): las `DocumentVersion`
duplicadas referencian el MISMO `SvgAssetId` que la original. `Project.ThumbnailAssetId` del
duplicado queda `null` (no se copia el puntero del original, para no mezclar la identidad de
ambos proyectos sobre el mismo Asset).

Detalle de implementación no trivial: `Project.CurrentVersionId -> DocumentVersion`,
`DocumentVersion.VectorDocumentId -> VectorDocument` y `VectorDocument.ProjectId -> Project`
forman un ciclo real entre tres filas NUEVAS si se intenta insertar el `Project` duplicado
con `CurrentVersionId` ya seteado en el mismo `SaveChangesAsync` -- EF Core no puede ordenar
un INSERT cíclico y lanza `InvalidOperationException` ("circular dependency"). Se resuelve
con DOS `SaveChangesAsync`: el primero inserta todo el grafo con `CurrentVersionId = null`
(sin ciclo), el segundo completa el puntero una vez que todas las filas ya existen.

## Validación de negocio (`ProjectService`)

- `Name`: requerido (`IsNullOrWhiteSpace` → 400 `invalid_name`), trimeado, máximo 200
  caracteres (`ProjectService.MaxNameLength`) -- número elegido por el implementador, el
  spec no fija uno ("longitud razonable").
- `Description`: opcional, máximo 2000 caracteres (`ProjectService.MaxDescriptionLength`).
- Semántica de `PATCH` elegida: un campo `null` en el body = "sin cambios" (no se toca ese
  campo). Para vaciar `Description` explícitamente, el cliente debe enviar `""` (string
  vacío, no `null`) -- se documenta como simplificación deliberada frente a JSON Merge
  Patch completo, fuera de alcance de esta tarjeta.

## Paginación/búsqueda/orden

`page`/`pageSize` (offset-based), `pageSize` por defecto 20, máximo 100
(`ProjectService.DefaultPageSize`/`MaxPageSize`, ambos valores elegidos por el
implementador, consistente con la recomendación del spec). `search` filtra por coincidencia
parcial case-insensitive de `Name` vía `EF.Functions.ILike` (se traduce a SQL, no trae todo
a memoria). `sortBy` acepta `LastModified` (default, `UpdatedAt` descendente), `Name`
(ascendente) o `Created` (`CreatedAt` descendente), case-insensitive; un valor no reconocido
devuelve 400 `invalid_sort` (decisión explícita: no silenciar a un default).

## Resultados de los 5 comandos de verificación

1. **`dotnet build`** (`backend/`): **0 errores, 0 advertencias.**
2. **`dotnet test`** (`backend/`, Testcontainers real, Docker disponible en el entorno):
   **705 passed, 0 failed, 0 skipped** (incluye los 17 tests nuevos de
   `ProjectRepositoryTests` + 11 de `ProjectV2EndpointsTests` = 28 tests nuevos de esta
   tarjeta, todos en verde; el resto -- 677 -- son los tests preexistentes de S01/S02 y de
   todo MVP1/MVP2, sin ninguna regresión).
3. **`pytest`** (`services/python-engine/`, `.venv` activo): **400 passed**, sin tocar
   ningún archivo Python (como se esperaba).
4. **`npm test -- --run`** (`frontend/`): **307 passed (36 test files)**, sin tocar ningún
   archivo de frontend (como se esperaba -- esta tarjeta no conecta la UI a la API nueva).
5. **`npm run build`** (`frontend/`): **0 errores** (`tsc -b && vite build` completo
   correctamente; el único warning es el pre-existente de tamaño de chunk, no relacionado
   con esta tarjeta).

## Confirmación: flujo clásico intacto

`git status`/`git diff` confirmados antes de reportar: los únicos archivos EXISTENTES
modificados son `Data/VectorizationDbContext.cs` (agrega el concurrency token de `Project`),
`Migrations/VectorizationDbContextModelSnapshot.cs` (regenerado automáticamente por
`dotnet ef migrations add`) y `Program.cs` (registra los servicios/endpoints nuevos + llama
al seeder). `Endpoints/ProjectEndpoints.cs`, `Projects/IProjectRegistry.cs`,
`Projects/ProjectRecord.cs`, `Projects/PersistentProjectRegistry.cs`,
`Projects/ProjectUploadService.cs` -- **ningún archivo del flujo clásico fue tocado**.

## Supuestos

- `MaxNameLength` = 200, `MaxDescriptionLength` = 2000: el spec pide "longitud razonable"
  sin fijar un número.
- `DefaultPageSize` = 20, `MaxPageSize` = 100: spec sugiere "ej. 100" como máximo: tomado
  literalmente; 20 como default es una elección propia, no especificada.
- PATCH: campo `null` = sin cambios (ver sección de validación arriba) -- JSON Merge Patch
  completo (distinguir "campo omitido" de "campo explícitamente null") queda fuera de
  alcance.
- `ThumbnailAssetId` del duplicado queda `null` (no se copia desde el original) -- el spec
  no lo menciona explícitamente, se documentó el razonamiento en la sección "Duplicate"
  arriba.
