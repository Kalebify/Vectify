# M2.2-S04 · Object Storage + gestión de Assets
URL: https://app.notion.com/p/3e8d77b2639881198efff4d059612025

Cuarta tarjeta de MVP 2.2. Conecta el `Asset` persistente (entidad EF de M2.2-S02, sin uso real todavía) con almacenamiento real de archivos, completando el principio "PostgreSQL guarda metadata, Object Storage guarda bytes" para el modelo NUEVO.

Stack: ASP.NET Core + Infra.

## Requerimiento (transcripción del cuerpo de la tarjeta)

### Objetivo (propiedad Notion)
Separar archivos pesados de PostgreSQL mediante IFileStorage y almacenamiento compatible con desarrollo local y producción.

### Criterio de aceptación (propiedad Notion)
Originales, previews, SVG y exports se guardan/leen/eliminan por IFileStorage; PostgreSQL contiene metadata y StorageKey, no blobs grandes.

### Principio
PostgreSQL guarda metadata; Object Storage guarda bytes.

### Abstracción
IFileStorage: SaveAsync, OpenReadAsync/ReadAsync, DeleteAsync, ExistsAsync y metadata necesaria.

### Desarrollo
Implementación local filesystem o MinIO/S3-compatible. Elegir y documentar. El dominio no conoce proveedor.

### Estructura sugerida
`projects/{projectId}/originals/{assetId}`; `previews/`; `vectors/`; `exports/`.

### Asset
Guardar storage key, mime, tamaño, checksum, dimensiones cuando aplique y tipo.

### Seguridad
No confiar en nombre de archivo del usuario; validar mime/extensión; impedir path traversal; límites de tamaño.

### Consistencia
Definir compensación cuando DB commit funciona y Storage falla o viceversa. Limpieza de huérfanos.

### Tests
Upload/read/delete, archivo inexistente, duplicado, fallo storage y reinicio.

### Definition of Done
Ninguna imagen/SVG grande necesita BYTEA para funcionar.

## ⚠️ Hallazgo clave (investigación previa del orquestador): `IFileStorage` YA EXISTE, no se reinventa

Desde `M1-S02`, `backend/Vectorify.Api/Storage/IFileStorage.cs` + `LocalFileStorage.cs` YA IMPLEMENTAN exactamente el principio que pide esta tarjeta: claves lógicas (no paths de filesystem), `SaveAsync`/`OpenReadAsync`/`ExistsAsync`, escritura atómica (temp+move), protección contra path traversal (rechaza segmentos `.`/`..`/caracteres inválidos), registrado como `Singleton` en `Program.cs` y consumido por **18 servicios/endpoints distintos** de todo el pipeline clásico (`PreprocessService`, `ColorPaletteService`, `VectorizationService`, etc.).

**Lo que falta, concretamente, es acotado**:
1. **`DeleteAsync`** -- no existe hoy en `IFileStorage` (el flujo clásico nunca borra un original). Esta tarjeta lo agrega.
2. **Checksum**: `StoredFile` (el record que devuelve `SaveAsync`) hoy solo tiene `Key`/`SizeBytes` -- falta `Checksum` si se quiere devolver calculado desde `SaveAsync` en vez de calcularlo aparte.
3. **Conectar `Asset` (M2.2-S02) con `IFileStorage` de verdad**: hoy `Asset.StorageKey` existe como columna pero NADA la escribe ni la lee -- esta tarjeta construye el servicio de aplicación (`AssetService` o similar) que: sube bytes vía `IFileStorage.SaveAsync`, persiste la fila `Asset` (StorageKey/MimeType/Size/Checksum/Width/Height/Type), lee vía `IFileStorage.OpenReadAsync`, borra vía `IFileStorage.DeleteAsync` + la fila (o la deja huérfana intencionalmente, ver Ambigüedades).
4. **Estructura de claves NUEVA** (`projects/{projectId}/originals/{assetId}`, `previews/`, `vectors/`, `exports/`) -- distinta de la convención hoy usada por el flujo clásico (`{projectId}/{imageId}/original.ext` y similares, cada registry con su propio `RootPath`/`XRegistry` en `appsettings.json`). Esta tarjeta NO migra las claves existentes del flujo clásico -- construye la convención nueva para `Asset`, usada únicamente por el código nuevo de esta tarjeta en adelante.

**Recomendación del orquestador (no opcional salvo justificación)**: **extender** `IFileStorage`/`LocalFileStorage` (agregar `DeleteAsync`, extender `StoredFile` con `Checksum`) en vez de crear una abstracción paralela -- es un cambio puramente aditivo (ningún consumidor existente se rompe, `LocalFileStorage` es la única implementación) y evita tener DOS abstracciones de storage conviviendo sin necesidad. "Implementación local filesystem o MinIO/S3-compatible, elegir y documentar": recomendación, seguir usando `LocalFileStorage` (ya probada, ya en producción conceptual del resto del pipeline) y documentar explícitamente en el ADR que MinIO/S3 queda diferido hasta que exista un requerimiento real de despliegue en la nube (mismo criterio que M1-S02 ya documentó "no almacenamiento cloud productivo" como fuera de alcance) -- la abstracción `IFileStorage` ya es agnóstica de proveedor, así que esta decisión no compromete nada a futuro.

## Estado actual (reutilizar, no reescribir)

- **`IFileStorage`/`LocalFileStorage`** (`backend/Vectorify.Api/Storage/`): ver arriba, extender, no reemplazar.
- **`Asset`** (`backend/Vectorify.Api/Data/Asset.cs`, M2.2-S02): columnas ya existen (`StorageKey`, `MimeType`, `FileName`, `Size`, `Width`/`Height` nullable, `Checksum`, `Type`), sin lectura/escritura real todavía.
- **`IProjectRepository`/`ProjectService`** (M2.2-S03): el `Project` ya tiene navegación `ICollection<Asset> Assets` -- un `AssetService` nuevo puede reusar el mismo `VectorizationDbContext` inyectado, mismo patrón de capas (Endpoint → Service → Repository/DbContext → EF Core).
- **Ownership**: cualquier endpoint nuevo de Assets debe seguir el MISMO criterio ya establecido en M2.2-S03 (`IUserContext.GetEffectiveUserId()`, 404 uniforme si el `Project` dueño del Asset no es del usuario efectivo -- nunca un 403 que revele existencia).
- **Validación de uploads YA EXISTENTE**: `backend/Vectorify.Api/Validation/` (usado por `ProjectEndpoints`/`UploadProjectImage` del flujo clásico) ya tiene lógica de validación de tipo MIME/tamaño de imagen -- revisar si es reusable tal cual o si esta tarjeta necesita su propia validación (Assets no son necesariamente solo imágenes: también SVGs/exports).

## Alcance de esta tarjeta

- [ ] **`IFileStorage.DeleteAsync(string key, CancellationToken)`** nuevo, implementado en `LocalFileStorage` (borra el archivo si existe, no lanza si ya no existe -- idempotente, mismo criterio que el resto de la interfaz).
- [ ] **`StoredFile` extendido** con `Checksum` (SHA-256 del contenido, calculado durante `SaveAsync` sin leer el archivo dos veces -- ej. un `CryptoStream`/`HashAlgorithm` mientras se copia al disco).
- [ ] **Convención de claves nueva para Assets**: `projects/{projectId}/{type}/{assetId}{extensión}` (adaptar la estructura sugerida de la tarjeta a algo consistente con `Asset.Type`/`Asset.FileName` ya existentes -- el implementador decide la forma exacta y la documenta).
- [ ] **`AssetService`/`IAssetRepository`** (mismo patrón arquitectónico que `ProjectService`/`IProjectRepository` de M2.2-S03): subir (bytes + metadata → `IFileStorage.SaveAsync` + fila `Asset`), leer (stream vía `IFileStorage.OpenReadAsync`, 404 si no existe), borrar (ver política de huérfanos en Ambigüedades).
- [ ] **Endpoints** (bajo `/api/v2/projects/{projectId}/assets`, mismo prefijo de versión que M2.2-S03 por consistencia -- no hay colisión real con rutas `/api/v1/` existentes para Assets, pero mantener el mismo prefijo evita fragmentar la API nueva): `POST` (subir), `GET /{assetId}` (descargar/stream), `DELETE /{assetId}`. Ownership vía `IUserContext`, mismo criterio 404 que M2.2-S03.
- [ ] **Seguridad**: nunca confiar en el nombre de archivo del usuario como parte de la clave de storage (ya lo hace `LocalFileStorage.ResolvePath`, pero el `AssetService` nuevo debe generar la clave desde `assetId`/`Type`, nunca desde `FileName` crudo). Validar MIME/extensión contra una lista permitida (reusar `backend/Vectorify.Api/Validation/` si aplica, o documentar por qué no). Límite de tamaño configurable (mismo patrón `IOptions<T>` que `UploadOptions.MaxFileSizeBytes` ya usa el flujo clásico).
- [ ] **Consistencia DB↔Storage**: definir y documentar explícitamente qué pasa si el `SaveAsync` a storage tiene éxito pero el `SaveChangesAsync` de la fila `Asset` falla (o viceversa) -- ver recomendación concreta en Ambigüedades. No hace falta una solución distribuida compleja (sagas/outbox) para esta tarjeta, pero sí una política clara y un mecanismo mínimo de limpieza de huérfanos.

## Fuera de alcance (explícito, respetar)
- NO migrar el flujo clásico (`LocalFileStorage` usado por Preprocess/Threshold/Vectorize/etc.) a la convención de claves nueva -- siguen con sus propias claves/registries tal cual.
- NO conectar la UI de React a estos endpoints nuevos -- mismo criterio que M2.2-S03 ("la API puede existir sin que el frontend la use todavía").
- NO implementar MinIO/S3 real -- queda documentado como diferido (ver recomendación arriba).
- NO Auth real más allá de lo que M2.2-S03 ya estableció (`DevelopmentUserContext`).

## Tests
- Upload → read → delete de un Asset, round-trip completo contra el storage real (filesystem, no un mock) y la fila `Asset` en PostgreSQL real (Testcontainers, mismo criterio que S01-S03).
- Leer un Asset inexistente → 404 (nunca una excepción no controlada).
- Subir dos Assets con el mismo `FileName` de usuario (pero contenido distinto) → ambos se guardan correctamente bajo claves DISTINTAS derivadas del `assetId`, nunca colisionan.
- Fallo simulado de storage durante el upload (ej. un `IFileStorage` fake que lanza `FileStorageException`) → la fila `Asset` NO queda huérfana en PostgreSQL (no se commitea si el storage falló).
- Reinicio: un Asset guardado antes de "reiniciar" (simulable recreando el `DbContext`/servicio sin tocar el storage real) sigue siendo legible después.
- Checksum: el valor devuelto por `SaveAsync` coincide con el hash real del contenido guardado.
- Seguridad: un `FileName` con intento de path traversal (`../../etc/passwd`) nunca afecta la clave de storage real generada (que depende de `assetId`, no del nombre).

## Definition of Done
Ninguna imagen/SVG grande necesita BYTEA para funcionar -- los Assets nuevos se suben/leen/borran de punta a punta vía `IFileStorage`, con metadata consultable en PostgreSQL.

## Umbrales de calidad
Mismo estándar de todo el proyecto: `dotnet build`/`dotnet test` (con Testcontainers), `pytest`, `npm test`, `npm run build` en verde -- esta tarjeta no debería tocar frontend/Python.

## Ambigüedades detectadas
- **Compensación DB↔Storage**: recomendación del orquestador -- en upload, guardar PRIMERO en `IFileStorage` (storage) y recién DESPUÉS insertar la fila `Asset` (DB). Si el storage falla, nunca se llega a crear la fila (nada que limpiar). Si el storage tiene éxito pero el `SaveChangesAsync` de la fila falla, queda un archivo huérfano en storage SIN fila en DB -- aceptable para esta tarjeta (documentar el trade-off: un archivo huérfano ocupa espacio pero no corrompe ningún dato ni rompe ninguna query, ya que nada referencia esa clave). Un mecanismo de limpieza de huérfanos (barrido periódico comparando claves de storage contra `Asset.StorageKey` en DB) queda fuera de alcance de esta tarjeta -- anotarlo como posible tarjeta de mantenimiento futura, no bloqueante del DoD actual.
- **Qué pasa con el archivo real al borrar un `Asset`**: la tarjeta pide `DeleteAsync` en la interfaz, pero M2.2-S03 ya estableció que el soft-delete de un `Project` NO borra sus Assets. Recomendación: un endpoint `DELETE` de Asset (cuando se llama explícitamente) SÍ borra el archivo real vía `IFileStorage.DeleteAsync` Y la fila -- es un hard delete intencional del Asset individual, distinto del soft-delete de `Project` (que no toca Assets en absoluto). Documentar esta distinción explícitamente.
- **Forma exacta de la convención de claves**: la tarjeta sugiere `projects/{projectId}/originals/{assetId}` pero no resuelve la extensión de archivo ni cómo distinguir `previews`/`vectors`/`exports` de `Asset.Type` (string libre desde M2.2-S02). Recomendación: `projects/{projectId}/{Asset.Type}/{assetId}.{extensión-derivada-del-mime}` -- el implementador decide el mapeo MIME→extensión (una tabla chica, ej. `image/png` → `.png`, `image/svg+xml` → `.svg`) y lo documenta.
- Ninguna otra ambigüedad bloqueante.

## Nota de orquestación
Cuarta tarjeta de MVP 2.2, corrida en modo autónomo (mismo criterio confirmado para las 10 tarjetas del milestone). El hallazgo clave de arriba (`IFileStorage` ya existe, se extiende) es la decisión más importante de esta tarjeta -- confirmarla explícitamente en el reporte final, igual que M2.2-S03 confirmó su decisión de ruta. Recordatorio: correr `npm run build` además de `npm test` aunque esta tarjeta no debería tocar frontend.
