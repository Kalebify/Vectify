# Vectify

Base ejecutable, testeable y reproducible sobre la que se construyen los MVP
de vectorización. Todavía **no** implementa vectorización real:

- **M1-S01**: el "esqueleto" y la comunicación entre los tres servicios.
- **M1-S02**: primer flujo funcional de entrada — cargar una imagen
  (PNG/JPG/WEBP) crea un proyecto y guarda el original sin procesarlo.
- **M1-S03**: preprocesamiento de imagen (escala de grises, contraste,
  brillo, reducción de ruido) con OpenCV en el motor Python, orquestado por
  la Web API, con preview cacheado por parámetros. Ver más abajo.

## Arquitectura

```
React (TypeScript + Vite)  --HTTP-->  ASP.NET Core Web API  --HTTP-->  Python + FastAPI
      frontend/                            backend/                services/python-engine/
```

Regla no negociable: **el navegador nunca llama directamente a Python.** Todo
pasa por la Web API de ASP.NET Core, que expone un cliente tipado
(`IPythonVectorizationClient`, vía `IHttpClientFactory`) hacia FastAPI y
compone un estado global (`online` / `degraded`) que consume React.

Como React y la Web API se sirven en orígenes distintos (puertos 5173 y 5080),
la Web API aplica una política CORS con los orígenes configurados en
`Cors__AllowedOrigins`. Si abres React desde otra URL o puerto, añádela ahí.

- **Frontend** (`frontend/`): React + TypeScript + Vite. Diagnóstico
  ("Home/Diagnostics") que consulta el estado de la Web API y del motor
  Python cada 5 segundos (`loading`, `online`, `degraded`, `error`), más el
  flujo de carga de imagen (M1-S02) y el panel de preprocesamiento (M1-S03)
  una vez que hay un proyecto creado.
- **Backend/orquestador** (`backend/`): ASP.NET Core Web API (Minimal APIs).
  Expone `/health` (liveness propio), `/api/v1/system/health` (estado
  compuesto) y `/api/v1/projects` (M1-S02: crea un proyecto a partir de una
  imagen y expone su original en `/api/v1/projects/{id}/images/{id}/original`).
  Nunca deja de responder aunque Python esté caído: si Python falla, el estado
  global pasa a `degraded` en vez de que la API se rompa.
- **Motor de procesamiento** (`services/python-engine/`): Python + FastAPI.
  Expone `/health`, `/api/v1/info` y `/api/v1/preprocess` (M1-S03: pipeline
  determinista con OpenCV — escala de grises, contraste, brillo, denoise).
  Estructura por `api/`, `services/`, `models/` y `core/`. Sin
  VTracer/Potrace todavía — eso es de sprints futuros.

## Requisitos

- [.NET SDK 9.0](https://dotnet.microsoft.com/download) (backend)
- [Node.js 24.x](https://nodejs.org/) y npm (frontend)
- [Python 3.12+](https://www.python.org/) (motor)
- [Docker](https://www.docker.com/) + Docker Compose (para levantar todo junto)

## Arranque con Docker (recomendado)

```bash
cp .env.example .env
docker compose up --build
```

Con la configuración por defecto:

- Frontend: http://localhost:5173
- Backend (Web API): http://localhost:5080 (`/health`, `/api/v1/system/health`, Swagger en `/swagger`)
- Motor Python: http://localhost:8001 (`/health`, `/api/v1/info`, docs en `/docs`)

`docker-compose.yml` monta un volumen nombrado (`vectify_backend_data`) en
`/app/App_Data` del contenedor `backend`, así que los originales
(`LocalFileStorage`) y los sidecars de metadata de proyecto
(`PersistentProjectRegistry`) sobreviven a `docker compose down`/restart.

Abrir http://localhost:5173 debería mostrar "API Online" y "Python Online".
Para probar la recuperación ante fallos:

```bash
docker compose stop python-engine   # React debe mostrar Python offline/degradado; el backend sigue respondiendo
docker compose start python-engine  # React debe volver a mostrar Python online
```

## Arranque en local (sin Docker)

En tres terminales separadas:

```bash
# 1) Motor Python
cd services/python-engine
python -m venv .venv
.venv/Scripts/activate        # en Windows; en Linux/macOS: source .venv/bin/activate
pip install -r requirements-dev.txt
cp .env.example .env
uvicorn app.main:app --reload --host 0.0.0.0 --port 8001

# 2) Backend
cd backend/Vectify.Api
dotnet run
# Sirve en http://localhost:5080 (ver Properties/launchSettings.json).
# PythonEngine:BaseUrl por defecto en appsettings.json apunta a http://localhost:8001.

# 3) Frontend
cd frontend
cp .env.example .env
npm install
npm run dev
# Sirve en http://localhost:5173 (o el puerto que informe Vite).
```

## Tests

```bash
# Backend (xUnit): cliente Python, integración HTTP con motor simulado,
# validación/almacenamiento/dimensiones, el endpoint de carga de imágenes,
# el registro de proyectos persistente y el endpoint de preprocesamiento (M1-S03)
cd backend
dotnet test

# Motor Python (pytest)
cd services/python-engine
pip install -r requirements-dev.txt
pytest

# Smoke HTTP (con la pila arriba, Docker o local; no verifica la UI)
BACKEND_URL=http://localhost:5080 PYTHON_URL=http://localhost:8001 \
  bash tests/e2e/smoke-test.sh

# E2E de carga de imágenes (M1-S02): arranca solo la Web API real (Python no
# participa en este sprint) y ejercita HTTP real de carga válida e inválida.
dotnet build backend/Vectify.sln
node tests/e2e/upload_e2e_test.mjs
```

Desde `frontend/`, ejecutar `npm ci`, `npm test`, `npm run build` y `npm run lint`.
Para verificar FastAPI real, desde la raíz y con el entorno Python activo:
`dotnet build backend/Vectify.sln` y `python tests/e2e/real_stack_test.py`.
Con Docker disponible: `python tests/e2e/docker_stack_test.py` construye una pila
isolada y verifica health, CORS y recuperación. Para smoke sin Bash:
`python tests/e2e/smoke_test.py`.

Ver `tests/README.md` para cobertura, comandos y resultados. **Docker y la
comprobación en navegador real siguen pendientes de verificación**; los tests
de React usan jsdom. El estado administrativo Done no acredita esos criterios.

## Variables de entorno

Cada servicio tiene su propio `.env.example`; la raíz tiene uno adicional
que alimenta `docker-compose.yml`. Ningún archivo `.env.example` contiene
secretos reales.

| Variable | Dónde | Default | Qué controla |
|---|---|---|---|
| `VITE_API_BASE_URL` | `frontend/.env` | `http://localhost:5080` | URL de la Web API que usa el navegador |
| `PythonEngine__BaseUrl` | `backend` (appsettings o env) | `http://localhost:8001` | URL del motor Python vista por ASP.NET Core |
| `PythonEngine__TimeoutSeconds` | `backend` (appsettings o env) | `5` | Timeout del cliente HTTP hacia Python |
| `Cors__AllowedOrigins` | `backend` (appsettings o env) | `http://localhost:5173,http://127.0.0.1:5173` | Orígenes del navegador (separados por comas) autorizados a llamar a la Web API |
| `Upload__MaxFileSizeBytes` | `backend` (appsettings o env) | `15728640` (15 MB) | Tamaño máximo aceptado en `POST /api/v1/projects` (supuesto: el spec no cuantifica un límite) |
| `Upload__AllowedContentTypes` | `backend` (appsettings o env) | `image/png,image/jpeg,image/webp` | MIME types aceptados en `POST /api/v1/projects` |
| `Storage__RootPath` | `backend` (appsettings o env) | `App_Data/uploads` | Carpeta local donde `LocalFileStorage` guarda los originales y los previews de M1-S03 (nunca se versiona) |
| `ProjectRegistry__RootPath` | `backend` (appsettings o env) | `App_Data/projects` | Carpeta donde `PersistentProjectRegistry` guarda un sidecar JSON por proyecto (nunca se versiona) |
| `Preprocess__MinContrast` / `Preprocess__MaxContrast` | `backend` (appsettings o env) | `0.5` / `3.0` | Rango válido de `contrast` en `POST .../preview` (supuesto: el spec no cuantifica valores) |
| `Preprocess__MinBrightness` / `Preprocess__MaxBrightness` | `backend` (appsettings o env) | `-100` / `100` | Rango válido de `brightness` en `POST .../preview` |
| `Preprocess__MinDenoise` / `Preprocess__MaxDenoise` | `backend` (appsettings o env) | `0` / `10` | Rango válido de `denoise` en `POST .../preview` |
| `Preprocess__TimeoutSeconds` | `backend` (appsettings o env) | `20` | Timeout del cliente HTTP hacia Python al generar un preview |
| `CORS_ALLOWED_ORIGINS` | `.env` (raíz) | `http://localhost:5173,http://127.0.0.1:5173` | Valor que docker-compose pasa a `Cors__AllowedOrigins`; si cambias `FRONTEND_PORT`, actualízalo |
| `SERVICE_NAME` / `SERVICE_VERSION` | `services/python-engine/.env` | `vectify-python-engine` / `0.1.0` | Identidad reportada en `/health` y `/api/v1/info` |
| `HOST` / `PORT` | `services/python-engine/.env` | `0.0.0.0` / `8000` | Bind del servidor uvicorn |
| `LOG_LEVEL` | `services/python-engine/.env` | `info` | Nivel de logging del motor |
| `FRONTEND_PORT` / `BACKEND_PORT` / `PYTHON_PORT` | `.env` (raíz) | `5173` / `5080` / `8001` | Puertos publicados por `docker-compose.yml` |
| `PYTHON_ENGINE_INTERNAL_URL` | `.env` (raíz) | `http://python-engine:8000` | URL interna (red de Docker) que usa el backend para llamar a Python |

## Contrato inicial (Python → ASP.NET Core)

`GET /health` en el motor Python responde:

```json
{ "status": "ok", "service": "vectify-python-engine", "version": "0.1.0" }
```

ASP.NET Core lo deserializa en un contrato tipado y compone
`GET /api/v1/system/health`:

```json
{
  "status": "online",
  "timestamp": "2026-01-01T00:00:00Z",
  "api": { "status": "online" },
  "python": { "status": "online", "service": "vectify-python-engine", "version": "0.1.0", "message": null }
}
```

Si Python está apagado, con timeout, o responde algo inválido, `python.status`
pasa a `unavailable` / `timeout` / `invalid_response` (o `error` para otros
códigos HTTP) y `status` global pasa a `degraded` — sin que la Web API deje
de responder 200.

## Carga y almacenamiento de imágenes (M1-S02)

`POST /api/v1/projects` recibe `multipart/form-data` con un campo `file`
(PNG, JPG/JPEG o WEBP; 15 MB máximo por defecto, ver `Upload:*` abajo — el
spec de la tarjeta no cuantifica un límite, así que este valor es un supuesto
documentado). Valida, en orden: archivo vacío/ausente, tamaño máximo, MIME
type + extensión permitidos, la firma binaria del contenido (descarta basura
obvia barato) y finalmente una decodificación real con ImageSharp (detecta
archivos truncados/corruptos que solo tienen una cabecera válida pero no son
una imagen completa). Si todo es válido, genera `projectId`/`imageId`, guarda
el original mediante `IFileStorage` (implementación local en desarrollo,
contrato preparado para sustituirse por almacenamiento S3-compatible sin
tocar el endpoint) y responde `201 Created` con:

```json
{
  "projectId": "…", "imageId": "…", "filename": "logo.png",
  "mimeType": "image/png", "bytes": 12345,
  "width": 800, "height": 600, "status": "uploaded"
}
```

`width`/`height` son `null` cuando el formato no permitió leerlos (lectura
best-effort de la cabecera, sin decodificar la imagen). El original se
recupera, sin modificarse, en `GET /api/v1/projects/{projectId}/images/{imageId}/original`.

Errores controlados (`400`, salvo `storage_failure` que es `500`), todos con
el cuerpo `{ "code": "...", "message": "..." }`:

| Code | Motivo |
|---|---|
| `empty_file` | No se envió archivo, o pesa 0 bytes |
| `file_too_large` | Supera `Upload:MaxFileSizeBytes` |
| `unsupported_format` | MIME type/extensión fuera de `Upload:AllowedContentTypes` |
| `corrupt_file` | El contenido no coincide con la firma binaria esperada, o no se pudo decodificar como imagen completa |
| `upload_interrupted` | La conexión se cortó o el formulario multipart no se pudo leer |
| `storage_failure` | Falló `IFileStorage` al guardar (disco, permisos, etc.) |
| `not_found` | `GET .../original` con un `projectId`/`imageId` que no existe |

El header opcional `Idempotency-Key` evita crear un proyecto duplicado ante
un reintento del mismo envío (doble click, retry tras un error de red que sí
llegó a completarse): si se repite la clave, la Web API responde `200 OK`
con el proyecto ya creado en vez de generar uno nuevo. El frontend
(`useImageUpload`) genera una clave (`crypto.randomUUID()`) al elegir un
archivo, la reutiliza entre reintentos del mismo archivo y la renueva al
elegir uno nuevo o al cancelar/resetear. La metadata de cada proyecto se
guarda en memoria (lecturas O(1)) y además se persiste como un sidecar JSON
en disco junto al original (`App_Data/projects/{projectId}/{imageId}.json`
por defecto, ver `ProjectRegistry:*` abajo); al reiniciar el proceso, la Web
API rehidrata el registro en memoria escaneando esos sidecars — sigue sin
haber una base de datos de negocio real. Ver Swagger (`/swagger`) para el
contrato completo y `backend/Vectify.Api/Vectify.Api.http` para ejemplos de
request.

## Preprocesamiento de imagen (M1-S03)

`POST /api/v1/projects/{projectId}/images/{imageId}/preview` recibe un JSON
con los parámetros del pipeline (`grayscale`, `contrast`, `brightness`,
`denoise`; rangos configurables vía `Preprocess:*`, ver variables de entorno
abajo — spec.md no los cuantifica, así que son un supuesto documentado, ver
`.sprint/3c1d77b2-6398-81a1-8df9-c2ecd0f5653f/spec.md`). La Web API valida
los rangos, y si ya existe un preview generado con exactamente esos
parámetros para esa imagen lo devuelve (`200 OK`, cacheado) en vez de volver
a llamar a Python; si no existe, reenvía el original (sin modificarlo) al
motor Python (`POST /api/v1/preprocess`, que aplica el pipeline determinista
con OpenCV), guarda el preview resultante bajo una nueva versión y responde
`201 Created` con:

```json
{
  "projectId": "…", "imageId": "…", "previewId": "…",
  "previewUrl": "/api/v1/projects/{projectId}/images/{imageId}/previews/{previewId}",
  "version": 1, "width": 800, "height": 600,
  "originalWidth": 800, "originalHeight": 600,
  "effectiveParams": { "grayscale": false, "contrast": 1.4, "brightness": 5, "denoise": 2 },
  "metrics": { "meanBrightness": 128.4, "stdDev": 42.1, "minValue": 0, "maxValue": 255 },
  "cached": false
}
```

"Resetear" es simplemente volver a llamar con los valores por defecto
(`grayscale=false, contrast=1.0, brightness=0, denoise=0`), que genera una
nueva versión de forma reproducible. El preview se recupera, en bytes, en
`GET /api/v1/projects/{projectId}/images/{imageId}/previews/{previewId}`.

Errores controlados con el mismo cuerpo `{ "code": "...", "message": "..." }`:

| Code | HTTP | Motivo |
|---|---|---|
| `invalid_parameters` | 422 | Algún parámetro está fuera de los rangos de `Preprocess:*` |
| `not_found` | 404 | `projectId`/`imageId` no existen (o el preview, en el GET) |
| `dimensions_exceeded` | 413 | La imagen supera el límite de dimensiones que acepta Python |
| `corrupt_file` | 400 | Python no pudo decodificar el original |
| `timeout` | 504 | El motor Python no respondió dentro de `Preprocess:TimeoutSeconds` |
| `engine_unavailable` | 503 | No se pudo contactar al motor Python |
| `invalid_response` | 502 | El motor Python respondió algo que la Web API no pudo interpretar |

Ver Swagger (`/swagger`) para el contrato completo.

## Fuera de alcance de este sprint

Quitar fondo, threshold avanzado, VTracer/Potrace, generación de SVG, base
de datos de negocio real, autenticación, editor vectorial, IA, detección de
colores, DXF, integración LightBurn y almacenamiento cloud productivo (la
abstracción `IFileStorage` está preparada para S3-compatible, pero solo
tiene implementación local en este sprint; lo mismo el registro de
proyectos, que persiste en sidecars JSON en disco — no en una base de datos
real).
