# IMPL.md — M2.2-S04 · Object Storage + gestión de Assets

Nota: este reporte lo escribió el orquestador a partir de la revisión personal del código (el
agente implementador no llegó a entregar su reporte final antes de que la sesión pidiera
cerrar la tarjeta con un PR) — el código fue leído completo, verificado de forma
independiente con los 5 comandos, y un bug real encontrado en la verificación fue corregido
antes de este cierre (ver "Bug encontrado y corregido").

## Hallazgo clave confirmado: `IFileStorage` extendido, no reinventado

`backend/Vectorify.Api/Storage/IFileStorage.cs`/`LocalFileStorage.cs` (existentes desde
M1-S02) ganaron:
- `Task DeleteAsync(string key, CancellationToken)` — idempotente (no lanza si la clave no
  tiene contenido).
- `StoredFile` extendido con `Checksum` (SHA-256 hex minúscula), calculado durante
  `SaveAsync` con un `CryptoStream` mientras se copia al disco (nunca una segunda lectura).

Los 2 test doubles existentes (`FakeFileStorage` en `PhysicalUnion`/`Projects`) se
actualizaron al mismo contrato nuevo. Los 18 consumidores reales de `IFileStorage` del flujo
clásico no se tocaron — `dotnet build`/`dotnet test` en verde lo confirma sin cambios de
comportamiento.

## Construcción nueva

- **`AssetKeyFactory`**: `projects/{projectId:N}/{type}/{assetId:N}{extensión}`. Tabla
  MIME→extensión: `image/png`→`.png`, `image/jpeg`→`.jpg`, `image/webp`→`.webp`,
  `image/svg+xml`→`.svg`.
- **`AssetService`/`IAssetService`**: capa de aplicación (mismo patrón `XResult`
  discriminado que `ProjectService` de M2.2-S03). Resuelve ownership reusando
  `IProjectRepository.FindByIdAsync` (nunca re-implementa el filtro de ownership).
- **`IAssetRepository`/`AssetRepository`**: acceso EF Core puro sobre `DbSet<Asset>`, sin
  resolver ownership por sí mismo (ya resuelto por `AssetService` antes de llamarlo).
- **`AssetUploadValidator`**: deliberadamente MÁS liviano que
  `Vectorify.Api.Validation.IImageUploadValidator` del flujo clásico (que decodifica la
  imagen completa con ImageSharp) — un Asset puede ser `image/svg+xml` (texto/XML, no algo
  que ImageSharp abra). Valida archivo vacío, tamaño máximo (`AssetOptions.MaxFileSizeBytes`)
  y MIME contra una lista permitida.
- **3 endpoints** bajo `/api/v2/projects/{projectId}/assets` (`POST` multipart, `GET
  /{assetId}` streaming, `DELETE /{assetId}`), mismo prefijo de versión que M2.2-S03.

## Política de consistencia DB↔Storage

Guarda PRIMERO en `IFileStorage` (storage), recién DESPUÉS la fila `Asset`. Si el storage
falla, nunca se crea la fila. Si el storage tiene éxito pero `SaveChangesAsync` falla, queda
un archivo huérfano en storage sin fila en DB — trade-off aceptado y documentado (no
corrompe datos, nada referencia esa clave). Limpieza de huérfanos queda fuera de alcance,
anotada como posible tarjeta de mantenimiento futura.

Delete es el camino inverso: borra el archivo real primero, solo si eso tiene éxito borra la
fila (para no perder la única referencia a una clave que todavía podría tener contenido).

## Seguridad

La clave de storage se deriva SIEMPRE de `assetId`/`type`/extensión-del-MIME — nunca del
`FileName` que mandó el usuario (que solo se guarda como metadata informativa en
`Asset.FileName`). `Asset.Type` se normaliza y valida contra un charset acotado
(`^[a-z0-9_-]{1,40}$`) antes de participar de la clave, evitando que un `type` arbitrario
introduzca segmentos de ruta inesperados.

## MinIO/S3: diferido

`LocalFileStorage` sigue siendo la única implementación de `IFileStorage`. Mismo criterio que
M1-S02 ya estableció ("no almacenamiento cloud productivo" fuera de alcance) — la abstracción
ya es agnóstica de proveedor, así que esta decisión no compromete nada a futuro.

## Bug encontrado y corregido durante la verificación independiente

`DeleteAsync` no era realmente idempotente: `File.Delete` lanza `DirectoryNotFoundException`
(no solo "silencio") cuando ni siquiera el directorio contenedor existe (caso real: borrar
una clave bajo un prefijo que nunca se usó). El código original solo atrapaba
`IOException`/`UnauthorizedAccessException`, dejando pasar `DirectoryNotFoundException` como
`FileStorageException` — confirmado con un test real que falló
(`DeleteAsync_WhenKeyWasNeverSaved_IsIdempotentAndDoesNotThrow`). Se agregó un
`catch (DirectoryNotFoundException) { }` explícito antes del catch genérico, tratando ambos
casos ("archivo no existe" y "directorio no existe") como el mismo resultado deseado ("esta
clave no tiene contenido"). Re-verificado en verde tras el fix.

## Verificación (5 comandos, confirmados de forma independiente por el orquestador)

1. `dotnet build` (backend/) → Compilación correcta, 0 advertencias, 0 errores.
2. `dotnet test` (backend/) → 722/722 (tras el fix de `DeleteAsync`; una corrida previa al
   fix mostró 1 fallo real, no flaky, reproducible).
3. `pytest` (services/python-engine/, .venv activo) → 400 passed.
4. `npm test -- --run` (frontend/) → 307 passed, 36 archivos.
5. `npm run build` (frontend/) → limpio, 0 errores de TypeScript.

## Archivos

Nuevos: `backend/Vectorify.Api/Assets/{AssetKeyFactory,AssetResult,AssetService,
AssetUploadValidator,AssetValidationResult,IAssetService,IAssetUploadValidator}.cs`,
`Assets/Persistence/{IAssetRepository,AssetRepository}.cs`, `Contracts/AssetResponse.cs`,
`Endpoints/AssetEndpoints.cs`, `Options/AssetOptions.cs`,
`Vectorify.Api.Tests/Assets/*`, `Vectorify.Api.Tests/EndToEnd/AssetEndpointsTests.cs`.

Modificados: `Storage/IFileStorage.cs`, `Storage/LocalFileStorage.cs`, `Program.cs` (DI +
`MapAssetEndpoints`), `Vectorify.Api.Tests/{PhysicalUnion,Projects}/FakeFileStorage.cs`,
`Vectorify.Api.Tests/Storage/LocalFileStorageTests.cs`.
