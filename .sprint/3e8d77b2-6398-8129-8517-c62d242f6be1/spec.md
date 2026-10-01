# M2.2-S02 · Modelo de datos y ERD del dominio
URL: https://app.notion.com/p/3e8d77b2639881298517c62d242f6be1

Segunda tarjeta de MVP 2.2. Construye sobre M2.2-S01 (`VectorizationDbContext` mínimo, con una única tabla marcador `SchemaProbe` descartable) agregando el modelo REAL de dominio persistente: `User`, `Project`, `Asset`, `VectorDocument`, `DocumentVersion`, `Layer`, `PaletteColor`.

Stack: ASP.NET Core + Vector + Infra.

## Requerimiento (transcripción del cuerpo de la tarjeta)

### Objetivo (propiedad Notion)
Diseñar e implementar el modelo persistente para User, Project, Asset, VectorDocument, DocumentVersion, Layer y Palette sin sobre-normalizar la geometría.

### Criterio de aceptación (propiedad Notion)
Existe ERD/ADR, entidades y migration con claves/índices/constraints; el modelo soporta ownership, proyectos, versiones, layers y assets.

### Antes de código
Crear ERD y ADR. Justificar normalización vs JSONB vs assets.

### Entidades mínimas
- **User**: Id, ExternalIdentityId nullable, Email nullable, DisplayName, CreatedAt.
- **Project**: Id, OwnerId, Name, Description, ThumbnailAssetId, CurrentVersionId, timestamps, DeletedAt.
- **Asset**: Id, ProjectId, Type, StorageKey, MimeType, FileName, Size, Width/Height opcionales, checksum, CreatedAt.
- **VectorDocument**: Id, ProjectId, WidthMm, HeightMm, ViewBox, SchemaVersion.
- **DocumentVersion**: Id, VectorDocumentId, VersionNumber, SvgAssetId/snapshot ref, Origin, MetadataJson, CreatedAt.
- **Layer**: Id, VersionId, ColorId, Name, Order, Visible, Locked, ManufacturingOperation.
- **PaletteColor**: Id, VersionId, Hex/RGB, Coverage, IsBackground, Order.

### Reglas
UUID/identificador estable; timestamps UTC; FK explícitas; índices OwnerId/UpdatedAt/ProjectId/Version; soft delete de Project si se adopta; unique VersionNumber por documento.

### Geometría
No crear tablas de nodos/puntos salvo evidencia. SVG canónico puede almacenarse como asset/snapshot; metadata consultable en DB.

### Definition of Done
Migration limpia desde cero y diagrama actualizado.

## Estado actual (reutilizar como referencia, no reescribir)

Esta tarjeta diseña el modelo PERSISTENTE nuevo — **no migra ni reemplaza ningún registry de archivos JSON existente todavía** (eso es responsabilidad de tarjetas posteriores de MVP2.2: `M2.2-S03 · Project Repository + API CRUD` empieza a consumir este modelo de verdad, `M2.2-S05 · Persistencia completa del VectorDocument` y `M2.2-S06 · Versionado persistente de documentos` lo completan). El objetivo de ESTA tarjeta es únicamente: ERD + ADR + entidades EF + migración limpia, sin wiring de aplicación todavía. Los conceptos de dominio ya existentes (archivo-por-versión, sin DB) que más se parecen a las entidades nuevas, para mapear nombres/campos con criterio en vez de inventar desde cero:

- **`Vectorify.Api.Projects.ProjectRecord`** (`backend/Vectorify.Api/Projects/ProjectRecord.cs`) — hoy el "proyecto" es solo `ProjectId`+`ImageId`+metadata del archivo subido (`PersistentProjectRegistry`). El `Project`/`Asset` nuevos de esta tarjeta son conceptualmente más ricos (ownership, nombre, descripción, thumbnail) — no hace falta que coincidan campo a campo con `ProjectRecord`, pero sí mantener los mismos nombres de concepto (`ProjectId` como identificador estable) para que `M2.2-S03` pueda razonar el mapeo sin ambigüedad.
- **`Vectorify.Api.ColorPalette.ColorGroup`** (`backend/Vectorify.Api/ColorPalette/ColorGroup.cs`) — el campo `PaletteColor` nuevo (Hex/RGB, Coverage≈`AreaPercent`, IsBackground≈`IsExcluded`, Order) es el equivalente persistente de este tipo. `ColorGroup` también tiene `RawGroupIds`/`MergedFrom`/`MaskStorageKey` (detalles de la PIPELINE de detección de Python) que NO corresponden a `PaletteColor` — esos siguen siendo responsabilidad exclusiva del flujo de detección en memoria/archivos, fuera de esta tarjeta.
- **`Vectorify.Api.VectorLayers.VectorLayer`** — el equivalente persistente es `Layer` (Order/Visible/Locked/ManufacturingOperation ya existen, hoy en el sidecar `LayerLayoutSetVersion` de M2.1-S07 + `ManufacturingOperationAssignment` de M2-S07 — dos sidecars de archivos separados que esta tarjeta puede, conceptualmente, unificar en una sola tabla `Layer` relacional, aunque la MIGRACIÓN real de esos datos existentes no es parte de esta tarjeta).
- **SVG/geometría**: hoy vive como archivo físico servido por `GET .../vectors/{vectorId}` (`LocalFileStorage`). La tarjeta es explícita: "SVG canónico puede almacenarse como asset/snapshot" — es decir, el `Asset`/`DocumentVersion.SvgAssetId` de este modelo referencia un archivo (vía `StorageKey`), NUNCA modela los `<path d="...">` como filas de una tabla. Esto es consistente con "Fuera de alcance" de M2.2-S01 (sin storage de binarios real todavía — `M2.2-S04` lo trae) y con "Geometría: no crear tablas de nodos/puntos salvo evidencia" de esta misma tarjeta.
- **`VectorizationDbContext`** (M2.2-S01, `backend/Vectorify.Api/Data/VectorizationDbContext.cs`) ya existe con `DbSet<SchemaProbe>` — esta tarjeta AGREGA los `DbSet`s nuevos al mismo `DbContext` (no crea uno paralelo). `SchemaProbe` queda tal cual por ahora (ninguna tarjeta pidió todavía removerlo; es inofensivo mientras exista).

## Alcance de esta tarjeta

- [ ] **ERD + ADR, ANTES de escribir código de las entidades**: un diagrama (puede ser Mermaid embebido en un `.md`, lo más simple de mantener versionado en el repo) + un documento de decisión (ADR corto) justificando explícitamente: por qué normalizar `PaletteColor`/`Layer` como tablas propias (acceso relacional, queries por proyecto/versión) en vez de guardarlos como JSONB dentro de `DocumentVersion` — y, al mismo tiempo, por qué la geometría SVG en sí NO se normaliza (se referencia como asset). Ambas decisiones conviven: parte del modelo se normaliza, parte se guarda como blob/asset — el ADR debe explicar el criterio de corte.
- [ ] **Entidades EF Core** (los 7 tipos mínimos listados arriba), como clases C# + configuración de `OnModelCreating` en `VectorizationDbContext` (mismo patrón ya establecido por `SchemaProbe` en M2.2-S01: `entity.ToTable(...)`, nombres de tabla en snake_case, `HasKey`, etc.).
- [ ] **Claves/relaciones**: UUID (`Guid`) como identificador estable en todas las entidades (mismo criterio que TODO el resto del proyecto ya usa — `GroupId`/`ProjectId`/`VectorId` son `Guid` en absolutamente todo el código existente). FKs explícitas (`Project.OwnerId → User.Id`, `Asset.ProjectId → Project.Id`, `VectorDocument.ProjectId → Project.Id`, `DocumentVersion.VectorDocumentId → VectorDocument.Id`, `Layer.VersionId → DocumentVersion.Id`, `PaletteColor.VersionId → DocumentVersion.Id`, `Project.ThumbnailAssetId → Asset.Id` nullable, `Project.CurrentVersionId → DocumentVersion.Id` nullable). Timestamps en UTC (`DateTimeOffset`, mismo tipo que ya usa todo el proyecto, ej. `LayerLayoutSetVersion.CreatedAt`).
- [ ] **Índices**: `OwnerId` (en `Project`), `UpdatedAt` (donde exista esa columna), `ProjectId` (en `Asset`/`VectorDocument`), `VersionNumber` único POR `VectorDocumentId` (constraint compuesto, no un índice único global).
- [ ] **Soft delete de `Project`**: columna `DeletedAt` nullable (ya está en la lista de campos mínimos) — la tarjeta dice "si se adopta": recomendación del orquestador, adoptarlo (la columna ya está pedida explícitamente en "Entidades mínimas", así que el criterio ya está decidido; lo que queda abierto es si los queries por defecto deben filtrar `DeletedAt == null` automáticamente — ver Ambigüedades).
- [ ] **Migración limpia desde cero**: `dotnet ef migrations add` generando las tablas nuevas (además de `schema_probes`, que queda intacto). Debe aplicarse limpiamente contra una base nueva (mismo criterio de verificación que M2.2-S01: Testcontainers).
- [ ] **Diagrama actualizado**: el ERD del punto 1 debe reflejar el modelo FINAL tal como terminó implementado (si algo cambió respecto del diseño inicial durante la implementación, el diagrama se actualiza antes de reportar terminado).

## Fuera de alcance (explícito, respetar)
- NO migrar/consumir desde la aplicación ningún dato real todavía (ningún endpoint nuevo, ningún repositorio, ninguna query real contra estas tablas) — eso es `M2.2-S03 · Project Repository + API CRUD`, la tarjeta siguiente. Esta tarjeta termina en: el modelo existe, migra limpio, está documentado.
- NO tocar ningún registry de archivos JSON existente (`PersistentProjectRegistry`, `PersistentLayerLayoutVersionRegistry`, etc.) — coexisten sin cambios, igual que estableció M2.2-S01.
- NO modelar nodos/puntos de geometría como filas — el SVG es un asset/snapshot referenciado, nunca tablas de paths.
- NO Auth real (`User.ExternalIdentityId` es solo una columna preparada para cuando `M2.2-S09 · User Ownership + DevelopmentUserContext` la use de verdad).
- NO storage real de binarios (`Asset.StorageKey` es solo una columna preparada para cuando `M2.2-S04 · Object Storage + gestión de Assets` la use de verdad).

## Tests
- Migración se aplica limpiamente desde cero contra PostgreSQL real (Testcontainers, mismo criterio que M2.2-S01 — nunca InMemory).
- Al menos un test de integración que, dentro de una transacción/DB de test, inserte una cadena completa relacionada (`User` → `Project` → `VectorDocument` → `DocumentVersion` → `Layer`/`PaletteColor`) y confirme que las FKs/constraints funcionan como se espera (ej. insertar un `Layer` con `VersionId` inexistente debe fallar por FK).
- Test de la constraint única de `VersionNumber` por `VectorDocumentId` (dos versiones con el mismo número para el MISMO documento deben fallar; el mismo número para documentos DISTINTOS debe estar permitido).
- Test de que los índices esperados existen (puede ser un test liviano contra el modelo de EF Core, no necesariamente contra Postgres real).

## Definition of Done
ERD + ADR documentados en el repo, entidades EF Core completas con relaciones/índices/constraints, migración que aplica limpiamente desde cero, diagrama reflejando el modelo final.

## Umbrales de calidad
Mismo estándar de todo el proyecto: `dotnet build`/`dotnet test` (incluyendo Testcontainers), `pytest`, `npm test`, `npm run build` en verde — esta tarjeta no debería tocar frontend/Python en absoluto, pero igual se confirman en verde.

## Ambigüedades detectadas
- **Formato del ERD**: no especificado. Recomendación del orquestador: Mermaid (`erDiagram`) embebido en un `.md` dentro de `.sprint/.../IMPL.md` o un doc dedicado bajo `backend/` — versionable como texto, se renderiza nativo en GitHub/Notion, sin depender de una herramienta externa de diagramación.
- **`DocumentVersion.MetadataJson` y `VectorDocument.ViewBox`**: la tarjeta sugiere JSONB para cierta metadata ("Justificar normalización vs JSONB vs assets") pero no especifica exactamente qué campos van en JSONB vs columnas propias. Recomendación: `ViewBox` (string simple, ej. "0 0 800 600") como columna de texto plana (se consulta poco, no justifica JSONB); `MetadataJson` de `DocumentVersion` como JSONB real (Npgsql soporta `jsonb` nativo) para metadata heterogénea/evolutiva que no amerita columnas propias todavía (ej. parámetros de la última vectorización) — documentar esta elección en el ADR.
- **Si el soft delete filtra automáticamente**: recomendación, usar un [global query filter de EF Core](https://learn.microsoft.com/ef/core/querying/filters) (`HasQueryFilter(p => p.DeletedAt == null)` en `Project`) para que CUALQUIER query futura (de `M2.2-S03` en adelante) excluya proyectos borrados por defecto, sin tener que recordar agregar el filtro en cada lugar — documentar que un `IgnoreQueryFilters()` explícito está disponible para los casos (si los hay) que sí necesiten ver borrados.
- **Relación circular `Project.CurrentVersionId → DocumentVersion.Id` y `DocumentVersion.VectorDocumentId → VectorDocument.Id → ProjectId`**: no es técnicamente circular (son dos cadenas de FK distintas: Project→Asset/DocumentVersion para "cuál es la actual", y VectorDocument→Project para "a qué proyecto pertenece"), pero el implementador debe prestar atención a los `DeleteBehavior` de EF Core para evitar un ciclo de cascada al borrar (`OnDelete(DeleteBehavior.Restrict)` donde corresponda, documentado caso por caso en el ADR).
- Ninguna otra ambigüedad bloqueante.

## Nota de orquestación
Segunda tarjeta de MVP 2.2, corrida en modo autónomo (mismo criterio confirmado para las 10 tarjetas del milestone). Construye directamente sobre la infraestructura de M2.2-S01 (mismo `VectorizationDbContext`, mismos patrones de test con Testcontainers). Recordatorio: correr `npm run build` además de `npm test` aunque esta tarjeta no debería tocar frontend.
