# Vectify

Base ejecutable, testeable y reproducible sobre la que se construyen los MVP
de vectorización. Todavía **no** implementa vectorización real:

- **M1-S01**: el "esqueleto" y la comunicación entre los tres servicios.
- **M1-S02**: primer flujo funcional de entrada — cargar una imagen
  (PNG/JPG/WEBP) crea un proyecto y guarda el original sin procesarlo.

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

- **Frontend** (`frontend/`): React + TypeScript + Vite. Pantalla única de
  diagnóstico ("Home/Diagnostics") que consulta el estado de la Web API y del
  motor Python cada 5 segundos y representa los estados `loading`, `online`,
  `degraded` y `error`.
- **Backend/orquestador** (`backend/`): ASP.NET Core Web API (Minimal APIs).
  Expone `/health` (liveness propio), `/api/v1/system/health` (estado
  compuesto) y `/api/v1/projects` (M1-S02: crea un proyecto a partir de una
  imagen y expone su original en `/api/v1/projects/{id}/images/{id}/original`).
  Nunca deja de responder aunque Python esté caído: si Python falla, el estado
  global pasa a `degraded` en vez de que la API se rompa.
- **Motor de procesamiento** (`services/python-engine/`): Python + FastAPI.
  Expone `/health` y `/api/v1/info`. Estructura por `api/`, `services/`,
  `models/` y `core/`. Sin OpenCV/VTracer/Potrace todavía — eso es de
  sprints futuros.

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
# validación/almacenamiento/dimensiones y el endpoint de carga de imágenes
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
| `Storage__RootPath` | `backend` (appsettings o env) | `App_Data/uploads` | Carpeta local donde `LocalFileStorage` guarda los originales (nunca se versiona) |
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
type + extensión permitidos y finalmente la firma binaria del contenido
(detecta corrupción). Si todo es válido, genera `projectId`/`imageId`, guarda
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
| `corrupt_file` | El contenido no coincide con la firma binaria esperada |
| `upload_interrupted` | La conexión se cortó o el formulario multipart no se pudo leer |
| `storage_failure` | Falló `IFileStorage` al guardar (disco, permisos, etc.) |
| `not_found` | `GET .../original` con un `projectId`/`imageId` que no existe |

El header opcional `Idempotency-Key` evita crear un proyecto duplicado ante
un reintento del mismo envío (doble click, retry tras un error de red que sí
llegó a completarse): si se repite la clave, la Web API responde `200 OK`
con el proyecto ya creado en vez de generar uno nuevo. Los proyectos viven en
memoria en este sprint (todavía no hay base de datos de negocio) y se
pierden al reiniciar la Web API. Ver Swagger (`/swagger`) para el contrato
completo y `backend/Vectify.Api/Vectify.Api.http` para ejemplos de request.

## Fuera de alcance de este sprint

Preprocesamiento de imagen, quitar fondo, threshold, OpenCV, VTracer/Potrace,
generación de SVG, base de datos de negocio, autenticación, editor vectorial,
IA, detección de colores, DXF, integración LightBurn y almacenamiento cloud
productivo (la abstracción `IFileStorage` está preparada para S3-compatible,
pero solo tiene implementación local en este sprint).
