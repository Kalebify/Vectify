status: ok

## M1-S07 · Simplificación de nodos

Implementa la reducción de nodos de un SVG ya vectorizado (M1-S05) como una
etapa propia y posterior del pipeline (módulo `Simplification/` en .NET,
`simplification_pipeline.py`/`simplification_service.py` en Python), con
preview reversible (sin persistir) y aplicar (crea una nueva versión, nunca
sobrescribe la anterior) — mismo patrón de caché+lock+historial versionado ya
usado en Preprocessing/Threshold/Vectorization, pero en un registro propio
(`SimplificationVersion`/`ISimplificationVersionRegistry`), no mezclado con
`VectorVersion`.

---

Archivos (Python — `services/python-engine`):
- `app/core/simplification_pipeline.py` — Douglas-Peucker iterativo (sin
  recursión, pila explícita) sobre subpaths M/L/Z; epsilon relativo a la
  diagonal del bounding box del SVG completo; técnica de "anillo abierto"
  (repetir el primer punto como ancla de cierre) para subpaths cerrados, con
  salvaguarda que deja el subpath intacto si la simplificación lo colapsaría
  a menos de 3 puntos (tolerancia extrema); comandos no soportados (`C`,
  arcos) se dejan intactos path por path.
- `app/services/simplification_service.py` — orquesta: límite de tamaño de
  entrada, decodificación UTF-8, re-sanitización defensiva del SVG de
  entrada (traduce `InvalidSvgError` interno a `InvalidInputSvgError` de
  cliente, 400), simplificación con timeout real (mismo patrón corregido de
  `_trace_with_timeout`: sin `with ThreadPoolExecutor(...)`, `shutdown(wait=False)`
  en el camino de timeout), re-sanitización del resultado, cálculo de
  nodeCount antes/después y % de reducción (clamp a [0,100]).
- `app/api/routes/simplify.py`, `app/api/dependencies.py` (mod), `app/main.py`
  (mod) — `POST /api/v1/simplify` (multipart `file` + `params` JSON) y mapeo
  de errores tipados a HTTP (400/413/422/500/504).
- `app/core/errors.py` (mod) — `InvalidInputSvgError` (400, problema del
  input del caller, distinto de `InvalidSvgError` que es 500/interno),
  `SvgInputTooLargeError` (413), `SimplificationTimeoutError` (504).
- `app/core/config.py` (mod) — `simplify_timeout_seconds` (reutiliza
  `max_svg_output_bytes` como límite de entrada, sin agregar config nueva).
- `app/models/schemas.py` (mod) — `SimplifyParams` (`epsilon_ratio`),
  `SimplifyMetrics` (reutiliza `VectorMetrics` para before/after),
  `SimplifyResponse`.
- `tests/test_simplification_pipeline.py` (12 tests) — curvas suaves
  (aproximación poligonal de un círculo), esquinas agudas preservadas,
  agujeros (2 subpaths en 1 `<path>`, ambos siguen cerrados), texto trazado
  (3 subpaths chicos, ninguno se pierde), tolerancias extremas (mínima no
  aumenta nodos; máxima 0.5 sigue produciendo un polígono cerrado válido de
  ≥3 puntos), comandos no soportados intactos, determinismo, SVG vacío.
- `tests/test_simplification_service.py` (11 tests) — reproducibilidad,
  reducción medible y consistente con antes/después, no muta bytes de
  entrada, SVG de entrada corrupto/no-UTF8/no-XML → `InvalidInputSvgError`,
  límite de tamaño de entrada, timeout real (con motor inyectable
  `simplify_fn`, mismo criterio que `FakeEngine` de vectorización) incluido
  el test de regresión de "retorna pronto aunque el hilo siga corriendo",
  sanitización de contenido peligroso.
- `tests/test_simplify_route.py` (9 tests) — contrato HTTP, determinismo,
  sin `<script>`, SVG corrupto → 400, `epsilon_ratio` fuera de rango → 422,
  params malformados → 422, entrada demasiado grande → 413, timeout → 504.

Archivos (Backend — `backend/Vectify.Api`):
- `Simplification/*` (13 archivos) — módulo propio y paralelo a
  `Vectorization/` (no se mezcla): `SimplificationParameters`/`Metrics`/`Version`,
  `ISimplificationVersionRegistry` + `InMemory`/`PersistentSimplificationVersionRegistry`
  (mismo patrón de sidecar JSON atómico que `PersistentVectorVersionRegistry`),
  `ISimplificationParameterValidator`/`SimplificationParameterValidator`
  (resuelve preset low/medium/high o tolerancia custom a un epsilon numérico
  antes de llamar a Python; rechaza si vienen ambos o ninguno),
  `ISimplificationService`/`SimplificationService` con dos flujos:
  `PreviewAsync` (sin caché ni registro, cada llamada invoca a Python de
  nuevo, el SVG completo viaja en la respuesta) y `ApplyAsync` (mismo patrón
  caché+lock+versionado que `VectorizationService`: cache-hit avanza versión,
  lock por clave de proyecto/imagen/vector de origen/parámetros).
- `Clients/IPythonSimplifyClient.cs`, `PythonSimplifyClient.cs`,
  `PythonSimplifyResult.cs` — cliente tipado dedicado con timeout propio y la
  misma validación defensiva adicional que `PythonVectorizeClient` (XML bien
  formado, Content-Type esperado, límite de tamaño, y específico de esta
  etapa: nodeCount después ≤ antes, % de reducción en [0,100]).
- `Contracts/SimplifyRequest.cs`, `SimplifyResponse.cs` (incluye
  `SimplifyPreviewResponse`/`SimplificationMetricsPayload`, reutiliza
  `VectorMetricsPayload`/`VectorBoundsPayload` ya existentes),
  `PythonSimplifyPayload.cs`.
- `Endpoints/SimplificationEndpoints.cs` — `POST .../simplify/preview` (200,
  nunca 201: no crea nada), `POST .../simplify/apply` (201 nueva versión /
  200 cache-hit), `GET .../simplifications/{id}`.
- `Options/SimplificationOptions.cs` (timeout, presets Low/Medium/High,
  rango de tolerancia custom), `Options/SimplificationRegistryOptions.cs`.
- `Program.cs`, `appsettings.json` (mod) — wiring de DI/HttpClient y sección
  `Simplification`/`SimplificationRegistry`.
- Tests: `Simplification/FakeVectorizationService.cs`,
  `Simplification/FakePythonSimplifyClient.cs`,
  `Simplification/SimplificationParameterValidatorTests.cs` (10 tests),
  `Simplification/SimplificationServiceTests.cs` (17 tests: preview
  reversible/sin caché, apply con caché+lock+versionado, nunca sobrescribe
  el SVG de origen, errores upstream), `Simplification/PersistentSimplificationVersionRegistryTests.cs`
  (5 tests, sobrevive "reinicio del proceso"), `EndToEnd/SimplificationEndpointsTests.cs`
  (9 tests, pipeline completo upload→preview→máscara→vector→simplificación),
  `TestSupport/SimplifyPayloads.cs`, `TestSupport/FakePythonPreprocessServer.cs`
  (mod: agrega ruta `/api/v1/simplify`).

Archivos (Frontend — `frontend/src`):
- `types/simplify.ts`, `api/simplifyApi.ts`, `hooks/useSimplify.ts` — estados
  idle/previewing/preview-ready/applying/applied/error; `requestPreview`
  dispara a pedido (nunca automático); `apply` solo persiste lo ya
  previsualizado; `cancel` es puramente local (sin llamada de red: nunca se
  persistió nada que deshacer).
- `lib/svgToDataUrl.ts` — convierte el SVG del preview (texto plano en la
  respuesta) en una data URL para `<img src>`, mismo criterio de defensa en
  profundidad que `VectorCanvas` (M1-S06): nunca se inyecta SVG inline en el
  DOM.
- `components/simplify/SimplifyControls.tsx` — radiogroup Bajo/Medio/Alto,
  nombre accesible explícito por input (`aria-label`) para no arrastrar el
  texto del hint en el nombre computado.
- `components/simplify/SimplifyComparison.tsx` — comparación lado a lado
  "Vector actual"/"Preview simplificado" reutilizando `useCanvasTransform`/`VectorCanvas`
  (M1-S06) directamente, en vez de reutilizar `VectorComparison` (que tiene
  las etiquetas "Original"/"SVG vectorizado" hardcodeadas) para no acoplar
  copy de otra etapa ni arriesgar sus tests.
- `components/simplify/SimplifyPanel.tsx` (+ test, 7 casos) — orquesta
  preset → preview (nodeCount antes/después, % reducción, comparación) →
  aplicar/cancelar.
- `components/vectorize/VectorizePanel.tsx` (mod) — nuevo prop opcional
  `onVectorReady`, mismo criterio que `ThresholdPanel.onMaskReady`.
- `App.tsx`, `App.css` (mod) — encadena la sección de simplificación tras un
  vector listo.

Dependencias agregadas: ninguna (Douglas-Peucker implementado a mano en
Python puro; no se agregó `shapely` ni ninguna librería de simplificación de
curvas — evaluado y descartado por no aportar sobre una implementación de
~40 líneas ya cubierta por tests).

Verificación (corrida de verdad):
- Backend: `dotnet build` → 0 errores, 0 warnings. `dotnet test` → **231/231
  pasaron** (191 base + 40 nuevos).
- Python: `pytest` → **167/167 pasaron** (135 base + 32 nuevos).
- Frontend: `tsc -b && vite build` → OK. `oxlint` → sin hallazgos. `npm test`
  (vitest) → **68/68 pasaron** (61 base + 7 nuevos).

## Decisiones de diseño y supuestos

- **Algoritmo: Douglas-Peucker** (elegido explícitamente sobre
  Visvalingam-Whyatt): estándar de facto para simplificar polilíneas,
  preserva esquinas/curvas pronunciadas (criterio de aceptación explícito:
  "esquinas (ángulos agudos)") de forma más directa de razonar/testear que
  un criterio basado en área de triángulos. Implementado a mano (iterativo,
  sin recursión) en `app.core.simplification_pipeline`, no con `shapely`
  (no era dependencia previa del proyecto y el algoritmo es simple/acotado —
  "no agregás dependencias que el diseño no requiere").
- **Epsilon relativo, no absoluto**: `epsilon = epsilon_ratio × diagonal del
  bounding box del SVG completo` (no por subpath individual), para que la
  tolerancia escale con el tamaño del diseño en vez de un valor fijo en
  píxeles que sería agresivo en un logo chico y casi nulo en uno grande.
- **Valores de los presets (no cuantificados por spec.md, supuesto
  documentado)**: Bajo = 0.15% de la diagonal, Medio = 0.4%, Alto = 1.2%
  (`Vectify.Api.Options.SimplificationOptions`, y espejados en
  `services/python-engine/app/core/config.py` como límites de
  `epsilon_ratio` aceptados: `(0, 0.5]`). Tolerancia numérica custom
  aceptada en el mismo rango `(0, 0.5]`.
- **Preservación de topología en subpaths cerrados**: se "abre" el anillo
  repitiendo el primer punto como ancla de cierre para Douglas-Peucker
  clásico (que necesita un inicio/fin fijos), y se descarta la coordenada
  duplicada al reconstruir el `d` (el `Z` final vuelve a cerrar). Si el
  resultado colapsaría a menos de 3 puntos (tolerancia extrema sobre una
  forma ya simple), se deja el subpath original sin cambios — prioriza
  nunca producir un path inválido sobre simplificar agresivamente ese caso
  puntual.
- **Comandos no soportados (`C`, arcos) se dejan intactos** path por path:
  el motor de vectorización actual (`VtracerEngine`, `mode="polygon"`) nunca
  los emite, así que en la práctica no aplica hoy, pero es la opción segura
  ante cualquier entrada futura/inesperada.
- **Preview nunca cachea ni persiste**: cada llamada a `/simplify/preview`
  invoca a Python de nuevo (no hay sección crítica que proteger, no escribe
  estado compartido) — "reversible" se logra por diseño (no hay nada que
  deshacer), no por un mecanismo de rollback.
- **`SimplificationVersion` es un tipo propio**, no una reutilización de
  `VectorVersion`: sigue el mismo *patrón* de historial versionado (nunca
  sobrescribe, cache-hit avanza versión) pero vive en su propio módulo/
  registro, referenciando `SourceVectorId` en vez de una máscara de origen.
  Ancho/alto se reutilizan directamente del `VectorVersion` de origen (la
  simplificación nunca cambia las dimensiones del lienzo), evitando un
  round-trip redundante a Python solo para confirmarlos.
- **Alcance**: la simplificación opera sobre una `VectorVersion` (M1-S05) ya
  creada; no soporta encadenar una simplificación sobre otra simplificación
  ya aplicada en este sprint (consistente con la redacción de la tarjeta:
  "opera sobre una VectorVersion ya creada"). Extenderlo sería agregar un
  segundo `sourceKind` al request, no un cambio de arquitectura.

## Excepciones/limitaciones conocidas

- Heredado de tarjetas anteriores: sin verificación en `docker compose up
  --build` ni navegador real (fuera del ciclo local de build/test).
- Edición manual de nodos y optimización específica de color: fuera de
  alcance explícito de spec.md, no implementados.

## Fix post-review: dos bugs en detección de "comandos no soportados"

Encontrados por revisión manual (no por el usuario) antes de commitear, y
reproducidos concretamente antes de pedir la corrección.

**Bug 1 (regex de tokenización incompleto)**: `_COMMAND_SPLIT_RE` solo
reconocía `M,L,C,Z` (y minúsculas) como límites de comando. Los comandos
`H,V,S,Q,T,A` no eran reconocidos, así que sus argumentos se mezclaban como
texto suelto con el comando anterior en vez de ser detectados como no
soportados — corrompiendo silenciosamente el path en lugar de dejarlo
intacto. Repro: `M10,10 H40 V40 H10 Z` (cuadrado válido) se convertía en
`M10,10 L40,40 Z` (línea/triángulo degenerado).

**Bug 2 (minúsculas relativas aceptadas como soportadas)**: `_is_simplifiable`
comparaba `command.upper() in _SUPPORTED_COMMANDS`, aceptando `m,l,z`
relativos como "soportados" pese a que el resto del pipeline trata todas las
coordenadas como absolutas (sin acumular offset), produciendo coordenadas
erróneas. Repro: `m10,10 l30,0 l0,30 z` (cuadrado válido en 10,10-40,40) daba
`M10,10 L30,0 L0,30 Z` (coordenadas cerca del origen, path distinto).

**Causa raíz común**: la detección de "comando soportado" no era estricta
sobre el conjunto exacto `{M,L,Z}` mayúscula, y el tokenizer no cubría el
alfabeto completo de comandos de path SVG — violaba la garantía explícita que
el propio módulo decía cumplir ("cualquier comando no soportado se deja
intacto"), aunque en producción no se disparaba hoy porque el único productor
(`VtracerEngine`, `mode="polygon"`) empíricamente solo emite M/L/Z absolutos
mayúscula (verificado en M1-S05). Quedaba latente ante un cambio de motor o
de versión de VTracer.

**Fix aplicado** (`app/core/simplification_pipeline.py`):
- `_COMMAND_SPLIT_RE` ahora reconoce todas las letras de comando SVG
  (`MLHVCSQTAZ` y minúsculas) como límites de token.
- `_is_simplifiable` compara `command in _SUPPORTED_COMMANDS` (sin
  `.upper()`), estrictamente `{"M","L","Z"}` mayúscula.
- Docstring y comentarios del módulo actualizados para reflejar que solo
  M/L/Z absolutos mayúscula son soportados; todo lo demás (incluyendo
  minúsculas) se deja intacto.

**Tests agregados** (`tests/test_simplification_pipeline.py`):
`test_simplify_leaves_h_and_v_commands_untouched`,
`test_simplify_leaves_lowercase_relative_commands_untouched`.

**Verificación independiente** (no solo lo reportado por el subagente):
reproduje ambos casos originales manualmente contra el código corregido — el
atributo `d` del `<path>` queda byte-a-byte idéntico al de entrada en ambos
casos (la única diferencia previa al comparar el SVG completo era formato de
serialización XML de `ElementTree`, no contenido). `pytest -q` en
`services/python-engine` → **169/169 pasaron** (167 previos + 2 nuevos).
Ningún archivo de .NET ni frontend fue tocado por este fix.
