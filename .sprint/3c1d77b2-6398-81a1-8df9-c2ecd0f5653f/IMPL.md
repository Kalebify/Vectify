status: ok

## Segunda ronda de corrección — 2026-09-25 (rama fix/3c1d77b2-preprocess-corrections)

El usuario probó manualmente el preprocesamiento y encontró 4 defectos más, verificados contra el código antes de corregir:

- **A. Brillo negativo incorrecto**: `adjust_contrast_brightness` usaba `cv2.convertScaleAbs`, que aplica `abs()` después de `alpha*in+beta` y antes de saturar — un píxel negro con brightness=-100 daba 100 en vez de 0. Arreglo: cálculo en `float32` + `np.clip(0,255)` sin abs.
- **B. Reset retrocedía la versión**: un cache-hit por parámetros devolvía el `PreprocessConfigRecord` viejo sin versionar de nuevo, así que `_latest` quedaba desincronizado del estado visible. Arreglo: cache-hit reutiliza los bytes/archivo ya generados pero crea y guarda un registro NUEVO con versión nueva (`NextVersion()` + `Save()`), separando el caché de píxeles del historial de versiones.
- **C. Race condition**: requests concurrentes con los mismos parámetros podían pasar el chequeo de caché a la vez y llamar a Python dos veces. Arreglo: `SemaphoreSlim` por clave `(projectId, imageId, parametersCacheKey)` serializa la sección crítica.
- **D. Bomba de descompresión**: se decodificaba la imagen completa antes de chequear dimensiones. Arreglo: `imagesize` lee ancho/alto de la cabecera sin decodificar píxeles y rechaza temprano; el chequeo post-decode se mantiene como red de seguridad.

**Verificación de esta ronda:**
- `dotnet build` → OK. `dotnet test` → **99/99 pasaron** (sin regresiones de rondas anteriores).
- `pytest` (con `.venv` existente, Python 3.12.14) → **43/43 pasaron**.

**Nota sobre el reporte de "punto 1" (licencia de ImageSharp 4.1.2)**: no se reprodujo contra el código de este repo — el `.csproj` en `main` y en todas las ramas de este sprint tiene `SixLabors.ImageSharp` fijado en `2.1.13` (Apache 2.0), nunca `4.1.2`, y no había cambios sin commit al momento del reporte. Se le pidió al usuario confirmar si probó desde un checkout separado.

---

**Reporte original de implementación (M1-S03), antes de las rondas de corrección:**

Archivos (nuevos):
- `services/python-engine/app/core/pipeline.py` — funciones puras deterministas: lectura segura, límites de dimensiones, grayscale, contraste/brillo, denoise (blur gaussiano), métricas, encode PNG.
- `services/python-engine/app/core/errors.py` — `CorruptImageError`, `DimensionsExceededError`, `InvalidParametersError` con código estable.
- `services/python-engine/app/services/preprocessing_service.py` — orquesta el pipeline (orden fijo: grayscale → contraste/brillo → denoise).
- `services/python-engine/app/api/routes/preprocess.py` — `POST /api/v1/preprocess` (multipart file + params JSON).
- `services/python-engine/tests/support.py`, `test_pipeline.py`, `test_preprocessing_service.py`, `test_preprocess_route.py` — golden images generadas en memoria, determinismo, límites de sliders, errores controlados.
- `backend/Vectify.Api/Options/PreprocessOptions.cs` — rangos de sliders + timeout.
- `backend/Vectify.Api/Preprocessing/*.cs` — `PreprocessParameters`, `PreprocessMetrics`, `PreprocessConfigRecord`, `IPreprocessConfigRegistry`/`InMemoryPreprocessConfigRegistry` (versionado + cache por parámetros), `IPreprocessParameterValidator`/`PreprocessParameterValidator`, `IPreprocessService`/`PreprocessService` (orquesta validación → cache → Python → storage → versión), `PreprocessResult`.
- `backend/Vectify.Api/Clients/IPythonPreprocessClient.cs`, `PythonPreprocessClient.cs`, `PythonPreprocessResult.cs` — cliente tipado dedicado (timeout propio) hacia `POST /api/v1/preprocess` de Python, sin excepciones sin controlar.
- `backend/Vectify.Api/Contracts/PreprocessRequest.cs`, `PreprocessResponse.cs`, `PythonPreprocessPayload.cs` — contratos JSON.
- `backend/Vectify.Api/Endpoints/PreprocessEndpoints.cs` — `POST /api/v1/projects/{id}/images/{id}/preview` y `GET .../previews/{previewId}`.
- `backend/Vectify.Api.Tests/Preprocessing/*.cs`, `Clients/PythonPreprocessClientTests.cs`, `EndToEnd/PreprocessEndpointsTests.cs`, `TestSupport/FakePythonPreprocessServer.cs`, `TestSupport/PreprocessPayloads.cs` — unitarios e integración (cache, versionado, rangos, errores 400/413/422).
- `frontend/src/types/preprocess.ts`, `frontend/src/api/preprocessApi.ts`, `frontend/src/hooks/usePreprocess.ts` — estado de parámetros + debounce (400ms) + llamadas a la Web API.
- `frontend/src/components/preprocess/ParameterControls.tsx`, `ImageComparison.tsx`, `PreprocessPanel.tsx`, `PreprocessPanel.test.tsx` — sliders, comparación original/procesado, loading, reset.

Archivos (modificados):
- `services/python-engine/app/core/config.py`, `app/models/schemas.py`, `app/api/dependencies.py`, `app/main.py` — settings de límites, schemas `PreprocessParams`/`PreprocessResponse`/`ErrorResponse`, exception handlers, router nuevo.
- `services/python-engine/requirements.txt` — `opencv-python-headless`, `numpy`, `python-multipart`.
- `services/python-engine/Dockerfile` — `libgomp1` (runtime de OpenCV headless).
- `backend/Vectify.Api/Program.cs`, `appsettings.json` — wiring de DI, sección `Preprocess`.
- `frontend/src/App.tsx`, `App.css`, `api/httpClient.ts` (agregado `postJson` + detección de abort), `components/upload/UploadPanel.tsx` (nuevo prop `onProjectCreated` para enlazar con el panel de preprocesamiento).

Dependencias agregadas:
- `opencv-python-headless==5.0.0.93` (Python) — pipeline OpenCV, requerido explícitamente por la tarjeta; headless porque no hay entorno gráfico.
- `numpy==2.5.3` (Python) — dependencia de opencv-python-headless para manipular arrays de imagen.
- `python-multipart==0.0.32` (Python) — requerido por FastAPI para `File`/`Form` en el endpoint multipart.
- Frontend/.NET: ninguna nueva.

Verificación (corrida de verdad):
- Python — build/tests: `pytest -q` (entorno portable Python 3.12.7 fuera del repo, no versionado) → **41 passed**.
- .NET — build: `dotnet build Vectify.sln` → OK, 0 advertencias, 0 errores.
- .NET — tests: `dotnet test Vectify.sln` → **88/88 pasaron**.
- Frontend — build/typecheck: `npm run build` (`tsc -b && vite build`) → OK.
- Frontend — lint: `npm run lint` (oxlint) → OK, exit 0.
- Frontend — tests: `npm test` (vitest) → **27/27 pasaron**.
- **`docker compose up --build` y navegador real: mismo estado pendiente que M1-S01/M1-S02** — esta máquina sigue sin Docker instalado; no se repitió la verificación en este ciclo.

Supuestos que se tuvieron que hacer (spec.md no los cuantifica):
- Rangos de sliders: contraste `[0.5, 3.0]` (factor multiplicativo), brillo `[-100, 100]` (offset aditivo), denoise `[0, 10]` (intensidad de blur gaussiano); grayscale es booleano. Duplicados intencionalmente en `PreprocessOptions` (.NET), `PreprocessParams` (Python, defensa en profundidad) y `frontend/src/types/preprocess.ts`.
- Algoritmo de denoise/suavizado: `cv2.GaussianBlur` con kernel `2*strength+1` y `sigma=strength*0.5` (determinista y rápido); no `fastNlMeansDenoising`.
- Contraste/brillo/normalización como una única transformación lineal (`cv2.convertScaleAbs`, alpha=contraste, beta=brillo).
- "Dimensiones excesivas": límite de 6000×6000 px y 25 megapíxeles totales, configurable.
- Timeout de preprocesamiento: 20s, distinto del timeout de 5s del health-check (cliente tipado dedicado `IPythonPreprocessClient`).
- Debounce de sliders en React: 400ms.
- Cache/versionado en memoria (mismo criterio que M1-S02, sin base de datos de negocio todavía); un preview se referencia por combinación exacta de parámetros (sin clamping — fuera de rango se rechaza); cada configuración nueva incrementa la versión de la imagen.
- Body JSON malformado devuelve el 400 `ProblemDetails` por defecto de ASP.NET Core; valores numéricos fuera de rango usan el `ApiErrorResponse{code:"invalid_parameters"}` propio.
- "Resetear" sin endpoint dedicado: React vuelve a pedir un preview con los valores por defecto (otra combinación de parámetros, con su propia versión/cache).

**Excepción que sigue abierta**: `docker compose up --build` y prueba en navegador real no se verificaron, igual que en M1-S01 y M1-S02 — esta máquina no tiene Docker instalado.
