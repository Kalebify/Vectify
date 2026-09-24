status: ok

Archivos (backend — ASP.NET Core):
- `backend/Vectify.Api/Threshold/ThresholdParameters.cs` — parámetros efectivos (Value 0-255, Invert) + clave de caché.
- `backend/Vectify.Api/Threshold/ThresholdRawMetrics.cs` — métricas crudas (foreground/background %) devueltas por Python.
- `backend/Vectify.Api/Threshold/ThresholdMetrics.cs` — métricas + clasificación de advertencia (IsNearEmpty/IsNearFull/WarningCode/WarningMessage) calculada por .NET.
- `backend/Vectify.Api/Threshold/ThresholdConfigRecord.cs` — versión guardada de configuración de threshold (mask, preview de origen, parámetros, storage key).
- `backend/Vectify.Api/Threshold/IThresholdConfigRegistry.cs` / `InMemoryThresholdConfigRegistry.cs` — historial versionado de máscaras por imagen, en memoria (mismo patrón que preprocesamiento).
- `backend/Vectify.Api/Threshold/IThresholdParameterValidator.cs` / `ThresholdParameterValidationResult.cs` / `ThresholdParameterValidator.cs` — valida rango de umbral y previewId requerido.
- `backend/Vectify.Api/Threshold/IThresholdService.cs` / `ThresholdResult.cs` / `ThresholdService.cs` — orquesta: localizar preview de origen (vía `IPreprocessService`), cache/lock por (proyecto, imagen, preview de origen, parámetros), llamada a Python, storage, versionado y clasificación de advertencia.
- `backend/Vectify.Api/Options/ThresholdOptions.cs` — rango de umbral, timeout y umbrales de advertencia "casi vacía/llena" (configurables).
- `backend/Vectify.Api/Contracts/ThresholdRequest.cs` / `ThresholdResponse.cs` / `PythonThresholdPayload.cs` — contratos JSON del endpoint y del motor Python.
- `backend/Vectify.Api/Clients/IPythonThresholdClient.cs` / `PythonThresholdResult.cs` / `PythonThresholdClient.cs` — cliente tipado hacia `POST /api/v1/threshold` de FastAPI, con timeout propio.
- `backend/Vectify.Api/Endpoints/ThresholdEndpoints.cs` — `POST .../threshold` y `GET .../masks/{maskId}`.
- `backend/Vectify.Api/Program.cs`, `appsettings.json` (modificados) — registra opciones/servicios/cliente/endpoints de Threshold.

Archivos (backend — tests):
- `backend/Vectify.Api.Tests/Threshold/ThresholdParameterValidatorTests.cs`, `ThresholdServiceTests.cs` (17 casos: not-found, validación, cache-hit, versionado en reset, concurrencia, distinto preview de origen no cachea entre sí, clasificación near-empty/near-full/sin advertencia, errores upstream), `FakePreprocessService.cs`, `FakePythonThresholdClient.cs`.
- `backend/Vectify.Api.Tests/Clients/PythonThresholdClientTests.cs` — mismos 9 estados que el cliente de preprocesamiento.
- `backend/Vectify.Api.Tests/EndToEnd/ThresholdEndpointsTests.cs` — integración HTTP completa (upload → preview → threshold → GET máscara), incluye advertencias near-empty/near-full.
- `backend/Vectify.Api.Tests/TestSupport/ThresholdPayloads.cs`, `FakePythonPreprocessServer.cs` (modificado, retrocompatible) — ahora también sirve `/api/v1/threshold` simulado.

Archivos (Python/FastAPI):
- `services/python-engine/app/core/threshold_pipeline.py` — funciones puras: reducción a 1 canal, `cv2.threshold` (BINARY/BINARY_INV), métricas foreground/background.
- `services/python-engine/app/services/threshold_service.py` — orquestación (lectura segura, chequeo de dimensiones por cabecera y post-decode, pipeline, codificación PNG).
- `services/python-engine/app/models/schemas.py` (modificado) — agrega `ThresholdParams`, `ThresholdMetrics`, `ThresholdResponse`.
- `services/python-engine/app/api/routes/threshold.py` — `POST /api/v1/threshold`.
- `services/python-engine/app/api/dependencies.py`, `app/main.py` (modificados) — wiring del nuevo router/servicio.
- `services/python-engine/tests/test_threshold_pipeline.py`, `test_threshold_service.py`, `test_threshold_route.py` — cubren claras/oscuras/transparencia/vacía/completa/repetibilidad (casos obligatorios del spec).

Archivos (frontend — React):
- `frontend/src/types/threshold.ts`, `frontend/src/api/thresholdApi.ts`, `frontend/src/hooks/useThreshold.ts` (debounce 400ms, reacciona al cambiar el preview de origen).
- `frontend/src/components/threshold/ThresholdControls.tsx`, `MaskComparison.tsx`, `ThresholdPanel.tsx` (+ `ThresholdPanel.test.tsx`) — slider de umbral, inversión, comparación, advertencia, métricas, reset.
- `frontend/src/components/preprocess/PreprocessPanel.tsx`, `App.tsx`, `App.css` (modificados) — encadena la etapa de threshold tras el primer preview listo.

Dependencias agregadas: ninguna (todo con los paquetes ya presentes en cada stack).

Verificación (corrida de verdad):
- build backend: `dotnet build` → OK, 0 errores/0 advertencias.
- tests backend: `dotnet test` → **138/138 pasaron**.
- build+typecheck+lint frontend: `npm run build` + `npm run lint` (oxlint) → OK.
- tests frontend: `npm test` (vitest) → **34/34 pasaron**.
- tests Python: `pytest` (vía `.venv` existente) → **80/80 pasaron**.

Decisiones de diseño y supuestos:
- **Threshold como etapa propia del pipeline, no un parámetro más de `PreprocessParams`**: módulos `Threshold/` paralelos a `Preprocessing/` en .NET, y `threshold_pipeline.py`/`threshold_service.py`/`routes/threshold.py` paralelos en Python. El request lleva el `previewId` de un preview ya preprocesado; `ThresholdService` lo localiza vía `IPreprocessService.FindPreview` y aplica el mismo patrón de caché + lock (ahora con clave `proyecto/imagen/previewDeOrigen/parámetros`) y "cache-hit produce versión nueva", corregido en la ronda anterior de M1-S03 y aplicado acá desde el principio.
- **Modo adaptativo: NO incluido.** El spec lo deja opcional ("si se valida técnicamente"). Decisión explícita de no implementarlo por alcance/tiempo — no una implementación parcial.
- **Foreground/background**: sobre la máscara binaria ya resultante (con inversión ya aplicada): foreground = píxeles en 255. Python calcula solo el porcentaje crudo; la clasificación "casi vacía/llena" es una regla de negocio en `Vectify.Api` (`ThresholdOptions`), no en Python.
- **Umbral de "casi vacía/llena"**: no cuantificado en el spec. Elegido ≤2% de foreground = "casi vacía", ≥98% = "casi llena".
- **Valor/rango por defecto del threshold**: `value=128`, rango `[0,255]`, no cuantificado en el spec — alineado entre `.NET`, Python y React.
- **Debounce de 400ms**, mismo valor no cuantificado ya usado en preprocesamiento, por consistencia.
- **Histograma**: no agregado (spec: "opcional, solo si aporta valor"); no se consideró necesario para el DoD.
- Sin persistencia más allá del registro en memoria — mismo criterio que M1-S03 (sin base de datos de negocio todavía).

**Excepción heredada**: `docker compose up --build` y navegador real siguen sin verificar (mismo motivo que las tarjetas anteriores).
