# Pruebas de Vectify

Ejecutar desde la raíz salvo que se indique otra carpeta.

## Backend

```bash
dotnet test backend/Vectify.sln
```

15 pruebas xUnit: 7 unitarias del cliente, 5 de integración HTTP y 3 de CORS.
La integración HTTP utiliza **Kestrel con respuestas simuladas**, no Python.
Cubre health, deserialización, caída, timeout y respuesta inválida. Kestrel
usa puertos efímeros y no depende de HttpListener/HTTP.sys de Windows.

## Frontend

Desde `frontend/`:

```bash
npm ci
npm test
npm run build
npm run lint
```

8 pruebas con Vitest, Testing Library y jsdom. Montan la pantalla real y
simulan fetch: loading, online, API offline, los cuatro fallos de Python y
el ciclo online → unavailable → online. Verifican los textos visibles.
jsdom no sustituye a un navegador real para comprobar CORS.

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

## Estado de verificación (2026-09-24)

- Backend: 15/15 aprobadas.
- Frontend: 8/8 aprobadas; build/TypeScript y lint correctos.
- Python: 3/3 aprobadas.
- Validador smoke: 3/3 aprobadas.
- Integración local con FastAPI real: online → unavailable → online correcta.
- Docker: no ejecutado porque no está instalado/disponible en este entorno.
- Navegador real: pendiente; las pruebas de interfaz actuales usan jsdom.

El sprint no tiene toda su Definition of Done verificada hasta completar
Docker y la comprobación en navegador, independientemente de la columna del tablero.
