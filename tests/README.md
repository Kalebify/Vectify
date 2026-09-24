# Pruebas

Este sprint tiene pruebas en tres niveles:

1. **Backend (xUnit)** — `backend/Vectify.Api.Tests/`. Unitarias del cliente
   `PythonVectorizationClient` (online, no disponible, timeout, respuesta
   inválida, error HTTP) e integración de extremo a extremo contra un
   servidor Python real (`WebApplicationFactory` + `HttpListener`), sin
   mockear el handler HTTP. Correr con `dotnet test` desde `backend/`.
2. **Motor Python (pytest)** — `services/python-engine/tests/`. Verifica
   `/health` y `/api/v1/info`. Correr con `pytest` desde
   `services/python-engine/` (con el entorno virtual activado).
3. **End-to-end (`tests/e2e/smoke-test.sh`)** — con la pila completa arriba
   (`docker compose up --build` o los tres servicios corriendo en local),
   confirma que ASP.NET Core realmente llama a FastAPI y que el flujo
   React → ASP.NET Core → FastAPI funciona de punta a punta.

Ver el README de la raíz del repo para instrucciones completas de arranque.
