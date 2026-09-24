# M1-S01 · Skeleton y comunicación .NET ↔ Python
URL: https://app.notion.com/p/3c1d77b263988171bcd6ec7c27f34f25
Stack declarado: MISSING (no existe propiedad "Tecnología"/"Stack" en el board "Sprints & Funcionalidades") — DERIVADO de la sección "Arquitectura oficial" del cuerpo de la tarjeta: **Frontend:** React + TypeScript + Vite · **Backend/orquestador:** ASP.NET Core Web API · **Motor de procesamiento:** Python + FastAPI. Flujo obligatorio: React → ASP.NET Core → FastAPI (el navegador nunca llama directo a Python).

Nota de alcance: esta tarjeta es scaffolding de backend/infraestructura, no un sitio web de marketing. Por decisión del usuario, se implementa directamente con `web-implementer` (build/tests locales como verificación), sin pasar por `web-design-architect` ni `web-qa-auditor` (Lighthouse/axe no aplican: no hay página pública que auditar).

## Requerimiento (transcripción del cuerpo de la tarjeta)

### Contexto
Este es el sprint fundacional. No implementa vectorización real: crea una base ejecutable, testeable y reproducible sobre la que se construirán todos los MVP posteriores.

### Arquitectura oficial
**Frontend:** React + TypeScript + Vite.
**Backend/orquestador:** ASP.NET Core Web API.
**Motor de procesamiento/vectorización:** Python + FastAPI.
Regla: el navegador nunca llama directamente a Python. El flujo es **React → ASP.NET Core → FastAPI**.

### Experiencia esperada
Al abrir la aplicación, una pantalla técnica mínima muestra el estado de la Web API y del motor Python. Si Python se apaga, React debe indicar que el motor no está disponible sin que la Web API deje de responder.

### Frontend — React
- Crear proyecto React + TypeScript + Vite separado del backend.
- Crear cliente HTTP centralizado para ASP.NET Core.
- Pantalla Home/Diagnostics con estado API y Python.
- Estados de loading, online, degraded y error.
- No implementar editor, upload ni vectorización.

### Backend — ASP.NET Core Web API
- Crear Web API ligera, preferentemente Minimal APIs para esta fase.
- Exponer `/health` y `/api/v1/system/health`.
- Implementar `IPythonVectorizationClient` mediante `IHttpClientFactory`/typed client.
- Configurar BaseUrl y timeouts por configuración/variables de entorno.
- Manejar Python offline, timeout, respuesta inválida y error HTTP sin excepciones sin controlar.
- Añadir logging estructurado y correlation/request ID.
- Exponer OpenAPI/Swagger en desarrollo.

### Microservicio — Python/FastAPI
- Crear servicio FastAPI independiente.
- Estructura por `api`, `services`, `models` y `core`.
- Exponer `/health` y `/api/v1/info`.
- Respuestas tipadas con versión y capabilities.
- Añadir pytest y documentación automática de FastAPI.
- Todavía no instalar/usar OpenCV, VTracer o Potrace salvo dependencias necesarias para preparar el proyecto.

### Contrato inicial
Python `/health` debe responder conceptualmente con `status`, `service` y `version`. ASP.NET deserializa la respuesta en un contrato tipado y compone el estado global para React.

### Infraestructura
- Repositorio con carpetas `frontend`, `backend`, `services`, `tests` y configuración Docker.
- Dockerfile por servicio cuando corresponda.
- `docker-compose.yml` debe levantar React, Web API y Python.
- `.env.example`; ningún secreto real versionado.
- URLs/puertos configurables; nada crítico hardcodeado.

### Pruebas obligatorias
- Health de ASP.NET devuelve OK.
- Health de Python devuelve OK.
- ASP.NET llama realmente a Python y deserializa respuesta.
- Python apagado produce estado degraded/unavailable controlado.
- Timeout de Python se controla.
- Respuesta Python inválida se controla.
- Frontend representa correctamente online/offline.
- `docker compose up --build` permite verificar el flujo completo.

### Definition of Done
1. Clonar el repositorio en un entorno limpio.
2. Ejecutar `docker compose up --build`.
3. Abrir React.
4. Ver API Online y Python Online.
5. Confirmar que ASP.NET realizó la llamada real a FastAPI.
6. Apagar Python y comprobar que React muestra Python Offline/Unavailable sin romper ASP.NET.
7. Reiniciar Python y comprobar recuperación.
8. Ejecutar suites de tests y obtener resultado correcto.
9. README explica requisitos, arranque, tests, arquitectura, URLs y variables.

### Fuera de alcance
No implementar upload, OpenCV, threshold, VTracer/Potrace, SVG, base de datos de negocio, autenticación, editor vectorial, IA, detección de colores, DXF ni integración LightBurn.

### Entregable
Una solución mínima pero profesional donde React, ASP.NET Core Web API y FastAPI funcionan conjuntamente y ASP.NET consume Python de forma tipada, resiliente y testeada.

## Criterios de aceptación

De la propiedad "Criterio de aceptación": "Frontend/API/Python arrancan localmente; health checks OK; .NET invoca Python y recibe respuesta tipada."

Ampliados por las secciones "Pruebas obligatorias" y "Definition of Done" del cuerpo (declarados explícitamente, no derivados):
- [ ] Health de ASP.NET Core devuelve OK (`/health`, `/api/v1/system/health`).
- [ ] Health de Python/FastAPI devuelve OK (`/health`, `/api/v1/info`).
- [ ] ASP.NET Core llama realmente a Python (`IPythonVectorizationClient`) y deserializa la respuesta tipada.
- [ ] Python apagado produce estado degraded/unavailable controlado en ASP.NET y en React, sin que ASP.NET caiga.
- [ ] Timeout de Python se controla sin excepción sin manejar.
- [ ] Respuesta inválida de Python se controla sin excepción sin manejar.
- [ ] Frontend (React) representa correctamente los estados loading/online/degraded/error.
- [ ] `docker compose up --build` levanta React + Web API + Python y permite verificar el flujo completo end-to-end.
- [ ] Suites de tests (backend y Python) corren y pasan.
- [ ] README explica requisitos, arranque, tests, arquitectura, URLs y variables de entorno.
- [ ] `.env.example` presente; ningún secreto real versionado.

## Referencias visuales / de marca
MISSING — no hay links ni adjuntos en la tarjeta. Coherente con el alcance: "una pantalla técnica mínima", no una pieza de marca/producto.

## Umbrales de calidad
No aplican los umbrales Lighthouse/axe por defecto del sistema (no hay página pública de producto en esta tarjeta; es scaffolding fundacional). Verificación sustituta: el ciclo local de `web-implementer` (build backend, build frontend, `pytest`, tests de ASP.NET, `docker compose up --build`) y la lista de "Pruebas obligatorias" / "Definition of Done" de arriba.

## Ambigüedades detectadas
- Propiedad "Tecnología" no existe en el board — no bloqueante: el stack está declarado explícitamente en la sección "Arquitectura oficial" del cuerpo (ver "Stack declarado" arriba).
- Esta tarjeta no encaja en el pipeline web (diseño/Lighthouse) para el que están armados `web-design-architect` y `web-qa-auditor` — resuelto por decisión explícita del usuario en el chat: se saltan esos dos pasos para esta tarjeta y se verifica con build/tests locales.
- Ninguna otra.
