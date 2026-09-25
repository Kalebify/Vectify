status: ok

## Segunda ronda de corrección — 2026-09-25 (rama fix/3c1d77b2-svg-security-persistence)

El usuario probó manualmente M1-S05/M1-S06 y encontró 3 defectos más que rastrean hasta esta tarjeta, verificados contra el código antes de corregir:

- **Sanitización de SVG permitía CSS externo (hueco de seguridad real)**: `<style>@import url(...)</style>` y `style="fill:url(...)"` con URLs externas sobrevivían la sanitización — solo se revisaban `href`/`xlink:href`, nunca `url(...)` dentro de CSS. Arreglo: `<style>` ahora se elimina por completo (elemento entero); atributos de presentación (`style`, `fill`, `stroke`, `clip-path`, `mask`, `filter`) con `url(...)` externo se eliminan, preservando `url(#interno)` legítimo.
- **Validación defensiva incompleta en `PythonVectorizeClient.cs`**: solo verificaba que `Svg`/`Bounds` no fueran null. Arreglo: valida XML bien formado con `<svg>` raíz, dimensiones/métricas positivas, bounds finitos y coherentes, Content-Type esperado, y límite de tamaño de respuesta (`MaxSvgResponseBytes`, default 10MB) — defensa en profundidad aunque Python ya valide del lado suyo.
- **`InMemoryVectorVersionRegistry` sin persistencia**: se perdía al reiniciar. Arreglo: `PersistentVectorVersionRegistry`, mismo patrón de sidecar JSON que `PersistentProjectRegistry`.

Verificación de esta ronda: `dotnet test` → 191/191, `pytest` → 135/135, sin regresiones.

**Timeout de VTracer sin matar el proceso**: reportado de nuevo, pero ya estaba documentado como limitación conocida desde el commit original (el hilo puede seguir corriendo en background; no hay forma de matarlo desde Python sin terminar el proceso). No se resolvió en esta ronda — requeriría aislar VTracer en un subproceso separado, un cambio de arquitectura mayor que el usuario decidió posponer.

---

Archivos (Python — `services/python-engine`):
- `app/core/vector_engine.py` — Protocol `VectorEngine` + `VtracerEngine`: encapsula VTracer (inversión de máscara, `mode="polygon"`, `hierarchical="stacked"`, documentados y verificados empíricamente), sin filtrar detalles del motor fuera de este módulo.
- `app/core/svg_processing.py` — `sanitize_svg` (quita `<script>`, `foreignObject`, `iframe`/`embed`/`object`/`audio`/`video`, handlers `on*`, `href`/`xlink:href` externos, rechaza DOCTYPE/ENTITY) y `compute_svg_stats` (paths, nodos aproximados, bounds) sobre el SVG ya sanitizado.
- `app/services/vectorization_service.py` — orquesta: límites por cabecera + post-decode, rechazo de máscara vacía, trazado con timeout real (ver corrección abajo), sanitización, límite de tamaño de salida, estadísticas.
- `app/api/routes/vectorize.py`, `app/api/dependencies.py`, `app/main.py` (mod) — endpoint `POST /api/v1/vectorize` y mapeo de errores tipados a HTTP (400/413/422/500/504).
- `app/core/errors.py`, `app/core/config.py`, `app/models/schemas.py` (mod) — nuevos errores tipados, `vectorize_timeout_seconds`, `max_svg_output_bytes`, `VectorBounds`/`VectorMetrics`/`VectorizeResponse`.
- `requirements.txt` (mod) — agrega `vtracer==0.6.15`.
- `tests/test_svg_processing.py`, `test_vector_engine.py`, `test_vectorization_service.py`, `test_vectorize_route.py` — siluetas simples, agujeros internos, bounds, máscara vacía, timeout (real, ver corrección), XML válido, determinismo.

Archivos (Backend — `backend/Vectify.Api`):
- `Vectorization/*` (11 archivos) — módulo propio, mismo patrón caché+versión+lock que Threshold (M1-S04), operando sobre `IThresholdService.FindMask`.
- `Clients/IPythonVectorizeClient.cs`, `PythonVectorizeClient.cs`, `PythonVectorizeResult.cs` — cliente tipado dedicado con timeout propio.
- `Contracts/VectorizeRequest.cs`, `VectorizeResponse.cs`, `PythonVectorizePayload.cs`, `Endpoints/VectorizationEndpoints.cs`, `Options/VectorizeOptions.cs` — contratos y endpoint, sin exponer nada específico de VTracer.
- Tests: `Vectorization/*Tests.cs` + fakes, `Clients/PythonVectorizeClientTests.cs`, `EndToEnd/VectorizationEndpointsTests.cs`.

Archivos (Frontend — `frontend/src`):
- `types/vectorize.ts`, `api/vectorizeApi.ts`, `hooks/useVectorize.ts` — estados idle/processing/success/error, disparo manual.
- `components/vectorize/VectorizePanel.tsx` (+ test) — botón "Vectorizar", render del SVG vía `<img>`, paths/nodos/bounds.
- `ThresholdPanel.tsx`, `App.tsx`, `App.css` (mod) — encadena la etapa de vectorización tras la primera máscara.

Dependencias agregadas: `vtracer==0.6.15` (motor elegido explícitamente por el usuario, ver "Decisión bloqueante resuelta" en spec.md).

Verificación (corrida de verdad):
- build backend: `dotnet build` → OK. tests backend: `dotnet test` → **170/170 pasaron**.
- build+typecheck+lint frontend: OK. tests frontend: `npm test` → **37/37 pasaron**.
- tests Python: `pytest` → **122/122 pasaron** (incluye la corrección de timeout, ver abajo).

## Corrección aplicada antes de cerrar la tarjeta (mismo ciclo, no una ronda separada)

Revisando el código antes de darlo por terminado, encontré que `_trace_with_timeout` usaba `with concurrent.futures.ThreadPoolExecutor(...) as executor:` — cuyo `__exit__` llama a `shutdown(wait=True)` **incluso mientras se propaga `TimeoutError`**, bloqueando el método (y el request HTTP completo) hasta que el hilo en background terminara de verdad. Con una entrada patológica que cuelgue a VTracer indefinidamente, el límite de tiempo configurado no habría servido de nada — contradice el requisito explícito del spec ("límites de ejecución"). El test original no lo detectaba porque el delay simulado (0.5s) era demasiado chico para notar el bloqueo extra.

Arreglo: manejo manual del executor, `shutdown(wait=False)` en el camino de timeout (retorna de inmediato) y `shutdown(wait=True)` en el camino feliz. Test nuevo que mide tiempo real: timeout=1s contra un engine que tarda 3s, verifica que el método retorna en <2s. Verificado que el test nuevo falla contra el código viejo (bloquearía ~3s) y pasa con el arreglo (~1s). **122/122 pytest** después del arreglo, sin regresiones.

Decisiones de diseño y supuestos:
- **Inversión de máscara para VTracer**: verificado empíricamente que `colormode="binary"` trata negro(0) como región a rellenar — inverso a la convención de Vectify (foreground=blanco). Se invierte antes de enviar, encapsulado dentro de `VtracerEngine`.
- **Máscara vacía = error controlado (422 `empty_mask`)**, no un SVG vacío "exitoso".
- **Determinismo**: verificado que VTracer es determinista byte a byte para la misma entrada/parámetros en los tests — no se encontró no-determinismo real.
- **Bounds** = bounding box del contenido dibujado (no el lienzo completo), calculado parseando `d`/`transform` de cada `<path>` sanitizado; `mode="polygon"` elegido para que este cálculo sea exacto.
- **Sin parámetros ajustables expuestos a React** en este sprint (el cuerpo de la tarjeta no los pide, a diferencia de Preprocess/Threshold) — `VectorParameters` queda como marcador vacío por consistencia/extensibilidad, sin violar YAGNI de forma bloqueante.
- **Cliente Python dedicado** (no reusa el de health-check) para no acoplar timeouts distintos.
- Límites no cuantificados por el spec: `vectorize_timeout_seconds=25` (Python), `Vectorize:TimeoutSeconds=30` (C#, mayor a propósito), `max_svg_output_bytes=5MB`.
- **Idempotencia de reintentos**: mismo patrón de caché por (máscara de origen + parámetros) que Threshold/Preprocess.

**Excepción heredada**: `docker compose up --build` y navegador real siguen sin verificar (mismo motivo que las tarjetas anteriores).
