# Pruebas de Vectify

Ejecutar desde la raíz salvo que se indique otra carpeta.

## Backend

```bash
dotnet test backend/Vectify.sln
```

49 pruebas xUnit:

- 15 de M1-S01: 7 unitarias del cliente Python, 5 de integración HTTP
  (health) y 3 de CORS. La integración HTTP utiliza **Kestrel con respuestas
  simuladas**, no Python. Kestrel usa puertos efímeros y no depende de
  HttpListener/HTTP.sys de Windows.
- 34 de M1-S02 (carga de imágenes): 11 unitarias de `ImageUploadValidator`
  (formato/tamaño/vacío/corrupto), 5 de `ImageDimensionsReader` (PNG/JPEG/WEBP),
  5 de integración de `LocalFileStorage` contra el filesystem real (directorio
  temporal), 4 unitarias de `ProjectUploadService` con storage fake, y 9 de
  integración HTTP de `POST /api/v1/projects` / `GET .../original` contra la
  Web API real (`WebApplicationFactory`), cubriendo carga válida, cada error
  controlado del spec e idempotencia.

## Frontend

Desde `frontend/`:

```bash
npm ci
npm test
npm run build
npm run lint
```

23 pruebas con Vitest, Testing Library y jsdom:

- 8 de M1-S01 (diagnóstico): loading, online, API offline, los cuatro fallos
  de Python y el ciclo online → unavailable → online.
- 7 unitarias de `validateImageFile` (validación UX de formato/tamaño/vacío).
- 8 de `UploadPanel`: selección válida/inválida, cancelar en seleccionado y
  en carga, progreso, éxito (proyecto creado), error controlado de la Web API
  y fallo de red — con `XMLHttpRequest` estubado (fetch no expone progreso de
  subida).

jsdom no sustituye a un navegador real para comprobar CORS ni drag-and-drop real.

## Python

Con Python 3.12+ y el entorno virtual activo:

```bash
pip install -r services/python-engine/requirements-dev.txt
cd services/python-engine
python -m pytest
```

3 pruebas de los endpoints FastAPI `/health` y `/api/v1/info`.

## Integración con FastAPI real (sin Docker)

Desde la raíz, con las dependencias Python instaladas:

```bash
dotnet build backend/Vectify.sln
python tests/e2e/real_stack_test.py
```

Arranca FastAPI y la Web API reales en puertos temporales, comprueba CORS y
que la API devuelve la identidad configurada del motor. Apaga Python,
comprueba `degraded`/`unavailable` con API online y reinicia Python para
comprobar recuperación sin reiniciar la API. Limpia sus procesos incluso
si falla. No arranca React ni afirma verificar la interfaz del navegador.

## Smoke HTTP de servicios ya iniciados

```bash
python tests/e2e/smoke_test.py
python tests/e2e/smoke_test.py --expected-python unavailable
python tests/e2e/test_smoke_validation.py
```

La segunda orden se usa **después de apagar Python**. Variables opcionales:
`BACKEND_URL`, `PYTHON_URL`, `FRONTEND_ORIGIN`. Defaults: localhost:5080,
localhost:8001 y http://localhost:5173. El script interpreta JSON y compara
por separado el estado global, API y Python, además de los headers CORS.
Las 3 pruebas del validador impiden que API online oculte Python offline.
El wrapper Bash `smoke-test.sh` requiere Python 3 (`PYTHON_EXECUTABLE` permite
seleccionar el intérprete); ya no depende de búsquedas de texto con curl.

## Docker

Con Docker Compose disponible, desde la raíz:

```bash
python tests/e2e/docker_stack_test.py
```

Construye un proyecto Compose aislado con nombre y puertos temporales,
comprueba frontend por HTTP, contrato, CORS y apagado/recuperación de Python.
Elimina exclusivamente ese proyecto al terminar. Requiere acceso a las
imágenes y registros de paquetes durante el build. No sustituye la revisión
visual de React:

```bash
docker compose up --build
# Abrir http://localhost:5173 y comprobar ambos servicios en línea.
docker compose stop python-engine
# Comprobar API en línea y Python no disponible.
docker compose start python-engine
# Comprobar recuperación en la misma página (polling cada 5 segundos).
```

## Estado de verificación

M1-S01 (2026-09-24):

- Backend: 15/15 aprobadas.
- Frontend: 8/8 aprobadas; build/TypeScript y lint correctos.
- Python: 3/3 aprobadas.
- Validador smoke: 3/3 aprobadas.
- Integración local con FastAPI real: online → unavailable → online correcta.
- Docker: no ejecutado porque no está instalado/disponible en este entorno.
- Navegador real: pendiente; las pruebas de interfaz actuales usan jsdom.

M1-S02 (2026-09-24), añadido sobre lo anterior:

- Backend: 49/49 aprobadas (15 de M1-S01 + 34 de carga de imágenes).
- Frontend: 23/23 aprobadas (8 de M1-S01 + 15 de carga de imágenes);
  build/TypeScript y lint correctos.
- `node tests/e2e/upload_e2e_test.mjs`: aprobado contra la Web API real
  (Kestrel, sin `WebApplicationFactory`) — carga válida con recuperación
  byte a byte del original, y los tres errores controlados principales
  (formato no soportado, archivo vacío, archivo corrupto).
- Docker y navegador real: mismo estado pendiente que M1-S01 (no ejecutado
  en este entorno); tampoco se verificó drag-and-drop real de la Dropzone
  fuera de jsdom.

El sprint no tiene toda su Definition of Done verificada hasta completar
Docker y la comprobación en navegador, independientemente de la columna del tablero.
