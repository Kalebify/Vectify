# M2.2-S03 · Project Repository + API CRUD
URL: https://app.notion.com/p/3e8d77b26398816398d6cda7b426dda4

Tercera tarjeta de MVP 2.2. Primera que CONSUME de verdad el modelo persistente de M2.2-S02 (`Project`, `VectorDocument`, `DocumentVersion`, etc. ya existen como entidades EF, migradas, pero sin ningún endpoint/repositorio/query real todavía). Esta tarjeta agrega la capa de aplicación (`ProjectService`/`IProjectRepository`) y la API CRUD completa sobre `Project`.

Stack: ASP.NET Core + Frontend (aunque el alcance real de esta tarjeta es backend puro -- ver "Fuera de alcance").

## Requerimiento (transcripción del cuerpo de la tarjeta)

### Objetivo (propiedad Notion)
Crear la capa de aplicación y API para listar, crear, abrir, renombrar, duplicar y eliminar proyectos persistentes.

### Criterio de aceptación (propiedad Notion)
CRUD de Project funciona contra PostgreSQL, filtra por OwnerId y devuelve DTOs versionados sin exponer entidades EF.

### API v1 (literal de la tarjeta)
```
POST /api/v1/projects
GET /api/v1/projects
GET /api/v1/projects/{id}
PATCH /api/v1/projects/{id}
DELETE /api/v1/projects/{id}
POST /api/v1/projects/{id}/duplicate
```

### Arquitectura
Endpoint → ProjectService/Application → IProjectRepository → EF Core. Evitar Endpoint/Controller → DbContext directo.

### Ownership
Toda operación recibe usuario efectivo mediante IUserContext. Un usuario no puede obtener un proyecto ajeno.

### Listado
Paginación, búsqueda por nombre y orden LastModified/Name/Created preparados desde API.

### Delete
Preferir soft-delete inicialmente si facilita recuperación; definir política explícita para assets.

### Duplicate
Nuevo ProjectId y nuevos IDs de documento/version/layers cuando corresponda; no duplicar identidad persistente.

### Tests
CRUD, not found, ownership, duplicate, soft delete, pagination y concurrencia básica.

### Definition of Done
La UI puede depender de una API real de proyectos.

## ⚠️ Conflicto real detectado (resolver, no ignorar): colisión de ruta `POST /api/v1/projects`

La tarjeta pide literalmente `POST /api/v1/projects` para CREAR un `Project` nuevo (persistente, con Name/Description/OwnerId). **Esa ruta YA EXISTE** (`backend/Vectorify.Api/Endpoints/ProjectEndpoints.cs` línea 25) y hace algo completamente distinto: es el endpoint de UPLOAD multipart del flujo clásico (MVP1), que crea un `ProjectRecord` (par `ProjectId`+`ImageId`, archivo subido) vía `PersistentProjectRegistry` -- NO tiene `OwnerId`/`Name`/`Description`, es un concepto de dominio completamente distinto que, por casualidad de nombres, se llama igual ("Project").

Registrar DOS `MapPost("/api/v1/projects", ...)` con la misma forma de ruta (sin segmentos adicionales que los diferencien) es un conflicto real de ASP.NET Core, no un detalle de estilo -- **no compila/arranca limpio o produce un comportamiento ambiguo en runtime**.

**Decisión del orquestador (no opcional, ejecutar tal cual salvo que el implementador encuentre algo mejor y lo justifique)**: la API CRUD nueva de esta tarjeta vive bajo **`/api/v2/projects`** (prefijo de versión nuevo), NO bajo `/api/v1/projects`. Razones:
- El `Project` nuevo (M2.2-S02) y el `ProjectRecord` del flujo clásico son modelos de dominio genuinamente distintos (ownership/nombre/descripción vs. par ProjectId+ImageId de un upload) -- no es una evolución compatible del mismo recurso, es un recurso nuevo que coincide en nombre.
- El flujo clásico (`App.tsx` hacia abajo, Upload → ColorPalettePanel → ... → Export) sigue siendo, por ahora, el único camino real de la UI (ver "Fuera de alcance") -- no se toca ni se re-rutea.
- `/api/v2/projects` dejar explícito en el propio nombre de ruta que es la API nueva, consistente con el "DoD" de esta tarjeta ("la UI PUEDE depender de..." -- implica que la integración real del frontend es de una tarjeta posterior, no ésta).
- El resto de las rutas (`GET /api/v2/projects`, `GET/PATCH/DELETE /api/v2/projects/{id}`, `POST /api/v2/projects/{id}/duplicate`) no tienen colisión real hoy, pero se agrupan bajo el mismo prefijo nuevo por consistencia.

Documentar esta decisión explícitamente en IMPL.md (no es un detalle menor, es una desviación deliberada del texto literal de la tarjeta, con justificación técnica real).

## Estado actual (reutilizar, no reescribir)

- **`VectorizationDbContext`** (M2.2-S01/S02) ya tiene `DbSet<Project>` con FKs/índices/soft-delete (`DeletedAt`, global query filter) completamente configurados -- esta tarjeta NO vuelve a tocar el modelo de entidades salvo que encuentre un gap real al implementar el repositorio (poco probable, S02 ya cubrió constraints/índices).
- **`PostgresOptions`/connection string**: mismo patrón ya establecido (M2.2-S01), sin cambios.
- **Patrón arquitectónico del resto del proyecto**: Endpoint (`Program.cs`/`Endpoints/*.cs`) → Service (`I*Service`) → Registry/Repository → almacenamiento. Esta tarjeta pide EXACTAMENTE ese mismo patrón para `Project` (`ProjectService` → `IProjectRepository` → EF Core), nombrado "Repository" en vez de "Registry" porque -- a diferencia de los registries de archivos JSON existentes -- esta vez sí hay una base relacional real detrás.
- **`IProjectRegistry`/`ProjectRecord`** (flujo clásico, `backend/Vectorify.Api/Projects/`) NO se tocan ni se renombran -- siguen existiendo tal cual, sirviendo al flujo clásico. El nombre "Project" para dos conceptos distintos (uno en memoria/archivo, otro en PostgreSQL) es una ambigüedad de nombres PRE-EXISTENTE a esta tarjeta (nadie la creó ahora) -- no es responsabilidad de esta tarjeta resolverla más allá de no empeorarla (de ahí la decisión de versión de ruta de arriba).

## Alcance de esta tarjeta

- [ ] **DTOs versionados**: nunca exponer `Vectorify.Api.Data.Project` (la entidad EF) directamente en una respuesta JSON -- un `ProjectResponse`/`ProjectSummaryResponse` (para el listado, más liviano) en `Contracts/`, mismo patrón que TODO el resto del proyecto ya usa (`ApiErrorResponse`, `LayerLayoutSetResponse`, etc.).
- [ ] **`IUserContext`** (mínimo, ver Ambigüedades): una abstracción que resuelve "el usuario efectivo de esta request" -- esta tarjeta necesita ownership REAL (filtrar por `OwnerId`, rechazar acceso a proyectos ajenos), pero Auth real es explícitamente `M2.2-S09 · User Ownership + DevelopmentUserContext` (tarjeta POSTERIOR). Implementar acá una versión mínima suficiente para que el filtrado por ownership sea real y testeable (no un no-op) -- ver recomendación concreta en Ambigüedades.
- [ ] **`IProjectRepository`** (interfaz) + implementación EF Core: `CreateAsync`, `FindByIdAsync(id, ownerId)`, `ListAsync(ownerId, paginación/búsqueda/orden)`, `UpdateAsync`, `SoftDeleteAsync`, `DuplicateAsync`. Siempre recibe/filtra por el `ownerId` resuelto desde `IUserContext` -- nunca un query sin ese filtro.
- [ ] **`ProjectService`**: capa de aplicación entre el endpoint y el repositorio -- valida reglas de negocio (ej. nombre no vacío, longitud razonable) antes de llegar al repositorio, traduce excepciones/resultados a lo que el endpoint necesita (mismo patrón `XResult` discriminado que usa el resto del proyecto, ej. `LayerLayoutResult`/`ColorPaletteResult`).
- [ ] **6 endpoints** bajo `/api/v2/projects` (ver la sección de conflicto de ruta arriba):
  - `POST /api/v2/projects` -- crea un `Project` nuevo (Name requerido, Description opcional, `OwnerId` = usuario efectivo). NO sube ningún archivo (eso sigue siendo responsabilidad del flujo clásico/de una tarjeta posterior que conecte ambos mundos).
  - `GET /api/v2/projects` -- lista, filtrado por `OwnerId` del usuario efectivo, con paginación + búsqueda por nombre + orden (`LastModified`/`Name`/`Created`) vía query params.
  - `GET /api/v2/projects/{id}` -- 404 si no existe O si existe pero pertenece a otro usuario (mismo comportamiento para ambos casos -- no filtrar información de existencia a un usuario no autorizado).
  - `PATCH /api/v2/projects/{id}` -- rename/actualizar descripción. Mismo criterio 404 de ownership.
  - `DELETE /api/v2/projects/{id}` -- soft-delete (setea `DeletedAt`, el global query filter de S02 ya lo excluye de queries normales). Ver política de Assets en Ambigüedades.
  - `POST /api/v2/projects/{id}/duplicate` -- nuevo `Project.Id`, nuevo `Name` (ej. "Copia de X"), copia Name/Description/OwnerId; si el proyecto origen tiene `VectorDocument`/`DocumentVersion`/`Layer`/`PaletteColor` asociados (hoy en la práctica NO los tiene todavía, ninguna tarjeta los crea aún, pero el código debe ser correcto para cuando sí existan), duplicarlos con IDs nuevos propios -- NUNCA reutilizar los IDs del original ("no duplicar identidad persistente").

## Fuera de alcance (explícito, respetar)
- NO conectar el flujo clásico de la UI a esta API nueva -- `App.tsx`/`UploadPanel`/etc. siguen usando el flujo de MVP1/MVP2 tal cual, sin cambios. El DoD dice "la UI PUEDE depender" (preparado, no obligatorio todavía).
- NO implementar Auth real -- `IUserContext` mínimo acá, reemplazo/extensión real en `M2.2-S09`.
- NO Object Storage real para Assets -- sigue siendo `M2.2-S04`. La política de "qué pasa con los Assets al soft-deletear un Project" es sobre todo conceptual/de datos en esta tarjeta (no hay archivos binarios reales todavía que limpiar).
- NO tocar `IProjectRegistry`/`ProjectRecord`/el flujo clásico de upload.

## Tests
- CRUD completo (crear, leer, actualizar, soft-delete) contra PostgreSQL real (Testcontainers, mismo criterio que S01/S02 -- nunca InMemory).
- 404 ante un ID inexistente.
- Ownership: un usuario no puede `GET`/`PATCH`/`DELETE` un proyecto de otro usuario (404, no 403 -- no revelar que el recurso existe).
- Duplicate: nuevo `Project.Id` distinto del original, mismos datos copiados, ningún ID del original reutilizado.
- Soft delete: un proyecto borrado no aparece en `GET /api/v2/projects` (lista) ni en `GET /api/v2/projects/{id}` (404), pero la fila sigue existiendo en la base (verificable con `IgnoreQueryFilters()` desde el propio test).
- Paginación: al menos un test con más resultados que una página, confirmando que corta correctamente y que un segundo "page" trae los restantes.
- Búsqueda por nombre y los 3 órdenes (`LastModified`/`Name`/`Created`).
- Concurrencia básica: dos actualizaciones concurrentes sobre el mismo proyecto no deben corromper datos (un test simple de dos `PATCH` interleaved, o un token de concurrencia optimista de EF Core -- el implementador elige el mecanismo y lo justifica).

## Definition of Done
La UI PUEDE depender de una API real de proyectos -- es decir, la API es correcta, completa y testeada de punta a punta, aunque ningún componente de React la consuma todavía en esta tarjeta.

## Umbrales de calidad
Mismo estándar de todo el proyecto: `dotnet build`/`dotnet test` (con Testcontainers), `pytest`, `npm test`, `npm run build` en verde -- esta tarjeta no debería tocar frontend/Python.

## Ambigüedades detectadas
- **Colisión de ruta `POST /api/v1/projects`**: resuelta arriba con una decisión explícita y no opcional (`/api/v2/projects`), no es una ambigüedad abierta para el implementador -- documentarla en IMPL.md.
- **`IUserContext` mínimo**: recomendación del orquestador -- una interfaz `IUserContext { Guid GetEffectiveUserId(); }` con una implementación `DevelopmentUserContext` que SIEMPRE devuelve el mismo `Guid` fijo (un usuario "dev" sembrado automáticamente la primera vez que se necesita, ej. al arrancar la API o lazily en el primer request -- el implementador decide cuál es más simple). Esto hace que el filtrado por `OwnerId` sea REAL (no un no-op: todo proyecto creado en esta sesión de desarrollo pertenece a ESE usuario fijo, y los tests pueden crear usuarios adicionales directamente contra la DB para probar el caso "proyecto ajeno" de verdad). `M2.2-S09` reemplaza/extiende esta implementación (su propio nombre, "DevelopmentUserContext", sugiere que literalmente continúa desde acá) sin que el resto del código (`ProjectService`/`IProjectRepository`) necesite cambiar, porque dependen de la abstracción `IUserContext`, no de la implementación concreta.
- **Política de Assets al soft-deletear un Project**: la tarjeta pide "definir política explícita" sin mandar una. Recomendación: los `Asset`s de un proyecto soft-deleteado NO se tocan (ni se borran ni se desvinculan) -- siguen existiendo en la tabla, simplemente inalcanzables a través de un `Project` que el query filter ya excluye. Esto es coherente con "preferir soft-delete... si facilita recuperación": si se restaura el proyecto (fuera de alcance de esta tarjeta un endpoint de restore, pero el dato en DB lo permitiría), sus Assets siguen intactos. La limpieza real de Assets huérfanos (hard delete después de X tiempo, storage real) es responsabilidad de `M2.2-S04` o una tarjeta de mantenimiento futura -- documentar esta decisión explícitamente en el ADR/IMPL.md.
- **Mecanismo de concurrencia optimista**: no especificado. Recomendación: una columna `RowVersion`/`xmin` (PostgreSQL tiene soporte nativo para concurrency tokens vía la columna de sistema `xmin`, que EF Core/Npgsql pueden mapear directamente sin agregar una columna propia) -- más simple que agregar un `byte[] RowVersion` manual. El implementador elige y justifica.
- **Forma exacta de la paginación** (`page`/`pageSize` vs `cursor`, límites máximos): no especificado -- recomendación, paginación simple basada en `page`/`pageSize` (offset-based, suficiente para el volumen esperado de proyectos por usuario, consistente con "50+ proyectos" que menciona `M2.2-S08` como referencia de escala), con un `pageSize` máximo razonable (ej. 100) para evitar abuso.
- Ninguna otra ambigüedad bloqueante.

## Nota de orquestación
Tercera tarjeta de MVP 2.2, corrida en modo autónomo (mismo criterio confirmado para las 10 tarjetas del milestone). Primera que empieza a dar USO real al modelo de M2.2-S02. El conflicto de ruta detectado arriba es la decisión más importante de esta tarjeta -- confirmarla explícitamente en el reporte final. Recordatorio: correr `npm run build` además de `npm test` aunque esta tarjeta no debería tocar frontend.
