status: ok

Resumen de lo construido para M1-S01 (repo estaba vacío salvo `.claude/` y `.sprint/`; se inicializaron los tres proyectos desde cero).

Archivos (agrupados por servicio; 76 archivos nuevos en total):

**Raíz**
- `README.md` — reescrito: arquitectura, arranque (Docker y local), tests, URLs, variables de entorno.
- `docker-compose.yml` — levanta frontend, backend y python-engine con puertos/URLs configurables por variables de entorno.
- `.env.example` — variables de orquestación Docker (puertos, `VITE_API_BASE_URL`, `PYTHON_ENGINE_INTERNAL_URL`, etc.), sin secretos.
- `.gitignore` — ignora `node_modules/`, `bin/`/`obj/`, `__pycache__/`, `.venv/`, `.env` reales (preserva los `.env.example`).
- `tests/README.md`, `tests/e2e/smoke-test.sh` — script de humo end-to-end (curl) que verifica que ASP.NET Core realmente llama a Python.

**`backend/`** (ASP.NET Core Web API, Minimal APIs, .NET 9)
- `Vectify.sln` — solución con los dos proyectos.
- `Vectify.Api/Program.cs` — endpoints `/health` y `/api/v1/system/health`, registro de `IHttpClientFactory` tipado, Swagger (Swashbuckle) en desarrollo, logging JSON estructurado.
- `Vectify.Api/Clients/{IPythonVectorizationClient,PythonVectorizationClient,PythonHealthCheckResult}.cs` — cliente tipado hacia FastAPI; traduce offline/timeout/JSON inválido/HTTP error a estados sin excepciones sin controlar.
- `Vectify.Api/Contracts/{PythonHealthPayload,SystemHealthResponse}.cs` — DTOs del contrato Python↔.NET y de la respuesta compuesta para React.
- `Vectify.Api/Options/PythonEngineOptions.cs` — `BaseUrl`/`TimeoutSeconds` configurables por `appsettings`/env vars.
- `Vectify.Api/Middleware/CorrelationIdMiddleware.cs` — correlation/request ID en headers y en el scope del logger.
- `Vectify.Api/appsettings.json`, `appsettings.Development.json`, `Properties/launchSettings.json`, `Vectify.Api.csproj`, `Vectify.Api.http` — configuración base (puerto 5080, sección `PythonEngine`).
- `Vectify.Api.Tests/Clients/PythonVectorizationClientTests.cs` — 7 unitarias con `HttpMessageHandler` stub (online, offline, timeout, JSON inválido, campos faltantes, error HTTP, status≠ok).
- `Vectify.Api.Tests/EndToEnd/HealthEndpointsTests.cs` + `TestSupport/FakePythonServer.cs` — 5 integración reales vía `WebApplicationFactory` + servidor HTTP real (`HttpListener`) simulando Python (online, offline, timeout, respuesta inválida) sin mockear el handler.
- `Dockerfile`, `.dockerignore` — build multi-stage SDK→runtime, puerto 8080 interno.

**`services/python-engine/`** (FastAPI)
- `app/main.py` — arma la app FastAPI, incluye routers.
- `app/api/routes/health.py` (`GET /health`), `app/api/routes/info.py` (`GET /api/v1/info`), `app/api/dependencies.py`.
- `app/core/config.py` — `Settings` (pydantic-settings) para host/puerto/nombre/versión/log level, todo por env var.
- `app/core/logging.py` — logging estructurado (JSON por línea).
- `app/models/schemas.py` — `HealthResponse`/`InfoResponse` tipados.
- `app/services/info_service.py` — lógica de salud/info, separada de las rutas.
- `tests/{conftest,test_health,test_info}.py` — 3 pytest (shape de `/health`, content-type, `/api/v1/info`).
- `requirements.txt`, `requirements-dev.txt`, `pytest.ini`, `.env.example`, `Dockerfile`, `.dockerignore`.

**`frontend/`** (React 19 + TypeScript + Vite, scaffold oficial `npm create vite@latest`)
- `src/api/httpClient.ts` — cliente HTTP centralizado hacia la Web API (nunca hacia Python).
- `src/api/systemApi.ts`, `src/types/system.ts` — llamada tipada a `/api/v1/system/health`.
- `src/hooks/useSystemHealth.ts` — polling cada 5s, traduce a `loading`/`online`/`degraded`/`error`.
- `src/components/{StatusPill,ServiceCard}.tsx` — UI de estado.
- `src/App.tsx`, `src/App.css`, `src/index.css`, `index.html` (lang="es"), `src/vite-env.d.ts` — pantalla única "Home/Diagnostics", sin editor/upload/vectorización.
- `Dockerfile`, `nginx.conf`, `.dockerignore`, `.env.example`, `README.md` (reescrito).
- `package.json`/`package-lock.json`/`tsconfig*`/`vite.config.ts`/`.oxlintrc.json` — generados por el scaffold, sin tocar convenciones.

Dependencias agregadas:
- Frontend: ninguna fuera de lo que trae `npm create vite@latest react-ts` (React 19, Vite 8, TypeScript 6, oxlint) — sin librerías de UI/animación.
- Backend: `Swashbuckle.AspNetCore 10.2.3` — Swagger UI en desarrollo, pedido explícitamente por el spec (el paquete nativo `Microsoft.AspNetCore.OpenApi` es incompatible en runtime con `Microsoft.OpenApi` 2.x que trae Swashbuckle ≥7, así que se usa solo Swashbuckle); `Microsoft.AspNetCore.Mvc.Testing 9.0.9` en el proyecto de tests, para `WebApplicationFactory`.
- Python: `fastapi 0.141.1`, `uvicorn[standard] 0.53.0`, `pydantic 2.13.5`, `pydantic-settings 2.15.0`, `python-dotenv 1.2.3` (runtime); `pytest 9.1.1`, `httpx 0.28.1` (dev/test).

Verificación (corrida de verdad):
- build backend: `dotnet build` (desde `backend/`) → ok, 0 errores, 0 advertencias.
- tests backend: `dotnet test` → **12/12 pasaron** (7 unitarias del cliente Python + 5 de integración real end-to-end contra un servidor HTTP real que simula Python).
- build frontend: `npm run build` (`tsc -b && vite build`) → ok, bundle generado (`dist/`).
- typecheck frontend: incluido en `npm run build` vía `tsc -b` → ok.
- lint frontend: `npm run lint` (oxlint) → ok, exit 0.
- tests python: `pytest` → **3/3 pasaron** (con un Python 3.12.7 portable instalado temporalmente en el scratchpad de la sesión; no quedó nada de esa instalación dentro del repo).
- Flujo real React→ASP.NET Core→FastAPI: verificado a mano corriendo backend y motor Python directamente (sin Docker): `/health` de ambos ok, `/api/v1/system/health` con Python arriba → `"status":"online"`; Python apagado → backend siguió respondiendo `/health` ok y `/api/v1/system/health` → `"status":"degraded"`; Python reiniciado → volvió a `"online"` sin reiniciar el backend.
- **`docker compose up --build`: NO EJECUTADO** — esta máquina no tiene Docker instalado (verificado con `Get-Command docker`, no encontrado). Los `Dockerfile` de los tres servicios y el `docker-compose.yml` están escritos, pero **no hay evidencia de ejecución real**. Este es un criterio de aceptación explícito de la tarjeta ("Pruebas obligatorias" ítem 8, Definition of Done ítems 2 y 4-7) que queda **sin verificar**.

Supuestos que se tuvieron que hacer:
- Node/npm estaban instalados en `C:\Program Files\nodejs` (se usó ruta completa). Python no estaba instalado en absoluto: se resolvió con una distribución portable descargada temporalmente fuera del repo, solo para correr `pytest`.
- Docker no está disponible en esta máquina — no se pudo ejecutar ni verificar `docker compose up --build`. Se compensó verificando el flujo completo (incluyendo apagado/reinicio de Python) corriendo backend y motor Python directamente, que ejercita la misma lógica de red que usarían los contenedores, pero **no es lo mismo que la verificación pedida explícitamente en la tarjeta**.
- Puertos elegidos (no especificados en el spec): frontend 5173, backend 5080 (interno 8080 en Docker), motor Python 8001 (interno 8000 en Docker) — documentados en el README y configurables por `.env`.
- `/api/v1/info` de Python responde `capabilities: ["health-check"]` (única capacidad real de este sprint fundacional).
- Se usó Swashbuckle.AspNetCore en vez del `Microsoft.AspNetCore.OpenApi` nativo del template, por conflicto real de versiones de `Microsoft.OpenApi` (rompía el build con `TypeLoadException` en ejecución).
