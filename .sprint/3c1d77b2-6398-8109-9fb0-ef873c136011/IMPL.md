status: ok

**Backend (ASP.NET Core) — nuevos archivos**
- `backend/Vectify.Api/Options/UploadOptions.cs` — límite de tamaño y MIME types permitidos, configurable.
- `backend/Vectify.Api/Options/LocalStorageOptions.cs` — carpeta raíz del storage local.
- `backend/Vectify.Api/Storage/IFileStorage.cs` — abstracción de storage (clave lógica), sustituible por S3-compatible.
- `backend/Vectify.Api/Storage/LocalFileStorage.cs` — implementación en disco (escritura atómica vía temp+move).
- `backend/Vectify.Api/Validation/{IImageUploadValidator,ImageUploadValidator,ImageSignature,ImageValidationResult}.cs` — valida vacío/tamaño/MIME+extensión/firma binaria (corrupción).
- `backend/Vectify.Api/Imaging/ImageDimensionsReader.cs` — lectura best-effort de width/height (PNG/JPEG/WEBP) sin dependencias nuevas.
- `backend/Vectify.Api/Projects/{ProjectRecord,IProjectRegistry,InMemoryProjectRegistry,IProjectUploadService,ProjectUploadService,ProjectUploadResult}.cs` — orquesta validación+storage+registro en memoria, con idempotencia por header.
- `backend/Vectify.Api/Contracts/{UploadImageResponse,ApiErrorResponse}.cs` — contratos tipados de respuesta/error.
- `backend/Vectify.Api/Endpoints/ProjectEndpoints.cs` — `POST /api/v1/projects` y `GET /api/v1/projects/{id}/images/{id}/original`.
- `backend/Vectify.Api/Program.cs` (modificado) — DI y mapeo de los nuevos endpoints.
- `backend/Vectify.Api/appsettings.json`, `Vectify.Api.http` (modificados) — config `Upload`/`Storage` y ejemplo de request.
- Tests nuevos: `Validation/ImageUploadValidatorTests.cs` (11), `Imaging/ImageDimensionsReaderTests.cs` (5), `Storage/LocalFileStorageTests.cs` (5), `Projects/{FakeFileStorage,ProjectUploadServiceTests}.cs` (4), `EndToEnd/ProjectEndpointsTests.cs` (9), `TestSupport/SampleImages.cs` (fixtures).

**Frontend (React) — nuevos archivos**
- `frontend/src/types/upload.ts` — contratos tipados espejo del backend.
- `frontend/src/lib/{validateImageFile,formatBytes}.ts` (+ test) — validación UX y formato de tamaño.
- `frontend/src/api/httpClient.ts` (modificado) — agrega `uploadFile` con progreso vía XHR y `ApiClientError` enriquecido.
- `frontend/src/api/projectsApi.ts` — `uploadProjectImage` / `getOriginalImageUrl`.
- `frontend/src/hooks/useImageUpload.ts` — máquina de estados idle/selected/invalid/uploading/success/error.
- `frontend/src/components/upload/{Dropzone,FilePreview,UploadProgress,ProjectCreatedCard,UploadPanel}.tsx` (+ `UploadPanel.test.tsx`, 8 tests) — flujo completo drag&drop/preview/progreso/cancelar/confirmar/proyecto creado.
- `frontend/src/test/FakeXMLHttpRequest.ts` — stub de XHR para testear progreso sin red real.
- `frontend/src/App.tsx`, `App.css` (modificados) — integra el panel de carga, un solo `h1`, jerarquía de encabezados sin saltos.

**E2E**
- `tests/e2e/upload_e2e_test.mjs` — arranca la Web API real (sin Python, que no participa) y ejercita HTTP real de carga válida (+recuperación byte a byte del original) e inválida (formato/vacío/corrupto). Ejecutado y verde.

**Documentación**: `README.md` y `tests/README.md` actualizados (contrato del endpoint, tabla de env vars, conteos de tests, nueva sección "Carga y almacenamiento de imágenes").

**Build/tests ejecutados de verdad:**
- `dotnet build backend/Vectify.sln` → compilación correcta, 0 advertencias, 0 errores.
- `dotnet test backend/Vectify.sln` → **49/49 pasaron**.
- `cd frontend && npm run build` (`tsc -b && vite build`) → build y typecheck ok.
- `npm run lint` (oxlint) → sin hallazgos.
- `npm test` (vitest) → **23/23 pasaron**.
- `node tests/e2e/upload_e2e_test.mjs` (contra `dotnet build` real, Kestrel) → `OK: E2E de carga de imágenes (válida e inválida) contra la Web API real.`

**Supuestos declarados** (spec no los cuantifica):
- Tamaño máximo de archivo: 15 MB (`Upload:MaxFileSizeBytes`), configurable por entorno.
- MIME types exactos: `image/png`, `image/jpeg`, `image/webp`, mapeados a extensiones `.png`/`.jpg,.jpeg`/`.webp`.
- La validación de tamaño ocurre después de que ASP.NET Core buffera el `IFormFile` completo (no hay corte a mitad de subida por tamaño; no se configuró `MultipartBodyLengthLimit` de Kestrel para evitarlo, dado el límite práctico de 15 MB).
- "Duplicación accidental" se resuelve con un header opcional `Idempotency-Key`: si se repite, la Web API devuelve el proyecto ya creado (200) en vez de crear uno nuevo. Sin ese header, cada `POST` crea un proyecto nuevo (mitigado en UI deshabilitando "Confirmar" durante la carga).
- No hay base de datos de negocio todavía: el registro de proyectos vive en memoria (`ConcurrentDictionary`) y se pierde al reiniciar la Web API — coherente con el alcance de M1-S01/M1-S02.
- Endpoint versionado elegido: `POST /api/v1/projects` (crea proyecto+imagen en un solo paso) y `GET /api/v1/projects/{projectId}/images/{imageId}/original` (recuperación), ya que el spec no fija la ruta exacta.
- Lectura de width/height es best-effort mediante parseo manual de cabeceras (sin agregar una librería de imágenes), documentado como "cuando estén disponibles"; si el parseo falla, se devuelve `null` sin bloquear la carga.
- **No se ejecutó verificación en Docker ni en navegador real** (mismo estado pendiente que M1-S01, documentado en `tests/README.md`); tampoco se probó drag-and-drop real fuera de jsdom.

No se tocó nada de `.sprint/` ni `.claude/`. No se hizo push/merge/deploy. No se agregaron dependencias nuevas (paquetes) — solo se usó lo ya presente en cada proyecto.
