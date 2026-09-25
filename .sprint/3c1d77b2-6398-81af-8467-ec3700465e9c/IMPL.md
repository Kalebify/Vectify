status: ok

## M1-S08 · Paths abiertos y líneas duplicadas

Implementa el primer "Laser Checker": un análisis de SOLO LECTURA sobre un
SVG ya vectorizado (M1-S05) o simplificado (M1-S07) que detecta (1) paths
que deberían estar cerrados pero no lo están y (2) segmentos/paths
duplicados o casi-duplicados, ambos con tolerancias relativas al tamaño del
diseño (unidades del modelo, no píxeles de pantalla ni relativas al zoom).
El checker nunca modifica el SVG ni persiste nada — sin caché, sin lock, sin
registro versionado (a diferencia de Preprocessing/Threshold/Vectorization/
Simplification) — cada corrida vuelve a analizar el SVG de origen desde
cero, determinista por construcción.

---

Archivos (Python — `services/python-engine`):
- `app/core/svg_path_parsing.py` (nuevo) — tokenización de comandos SVG
  (`tokenize_path_d`, `extract_subpaths`, `is_supported_path`, `local_name`)
  EXTRAÍDA de `simplification_pipeline.py` (M1-S07) a un módulo compartido:
  una sola fuente de verdad para el criterio "solo M/L/Z absolutos en
  mayúscula son soportados", reutilizada ahora también por el nuevo
  `path_checker.py` — evita repetir el tokenizer (y arriesgar reintroducir
  los dos bugs de tokenización ya corregidos en M1-S07, documentados en el
  reporte de ese sprint) en un segundo lugar.
- `app/core/simplification_pipeline.py` (mod) — refactor puro: usa las
  funciones de `svg_path_parsing`, sin cambio de comportamiento (verificado:
  los 13 tests de `test_simplification_pipeline.py` siguen pasando byte a
  byte sin tocarlos).
- `app/core/path_checker.py` (nuevo) — `analyze_svg_paths(sanitized_svg,
  close_gap_ratio, duplicate_point_ratio, max_subpaths)`: recorre el
  documento en orden (`<path>` en orden de aparición, subpaths dentro de
  cada uno en orden de aparición), detecta subpaths abiertos "casi
  cerrados" (`_detect_open_paths`) y clústeres de subpaths duplicados/casi-
  duplicados vía Union-Find (`_detect_duplicates`, comparación punto a
  punto en el mismo sentido de recorrido o invertido). `<path>` con
  comandos no soportados se excluyen del análisis por completo (ni falso
  positivo ni falso negativo sobre datos que no se pueden interpretar con
  seguridad) y se cuentan en `skipped_path_count`. Salvaguarda de
  rendimiento: `TooManySubpathsError` si la cantidad de subpaths
  analizables supera `max_check_subpaths` (la detección de duplicados es
  O(n²)).
- `app/services/path_checker_service.py` (nuevo) — orquesta: límite de
  tamaño de entrada (reutiliza `max_svg_output_bytes`), decodificación
  UTF-8, re-sanitización defensiva del SVG de entrada (mismo criterio que
  `SimplificationService`), análisis con timeout real (mismo patrón
  corregido de `_check_with_timeout`: sin `with ThreadPoolExecutor(...)`),
  conversión de los dicts del core a los modelos Pydantic tipados.
- `app/api/routes/check.py` (nuevo), `app/api/dependencies.py` (mod),
  `app/main.py` (mod) — `POST /api/v1/check` (multipart `file` + `params`
  JSON) y mapeo de errores tipados a HTTP (400/413/422/500/504).
- `app/core/errors.py` (mod) — `CheckTimeoutError` (504),
  `TooManySubpathsError` (413).
- `app/core/config.py` (mod) — `check_timeout_seconds` (15s, mismo criterio
  que `simplify_timeout_seconds`), `max_check_subpaths` (20 000, cota de la
  detección O(n²) de duplicados).
- `app/models/schemas.py` (mod) — `CheckParams` (`close_gap_ratio`,
  `duplicate_point_ratio`, con defaults), `CheckBounds`, `OpenPathIssue`,
  `DuplicateMember`, `DuplicatePathIssue`, `CheckIssue` (unión discriminada
  por `type`), `CheckSummary`, `CheckResponse`.
- `tests/test_path_checker.py` (18 tests) — diseño correcto (cero issues),
  path abierto chico/grande, gap fuera de tolerancia no se dispara, subpath
  ya cerrado nunca se marca, dos casos de falso positivo conocidos
  verificados (línea degenerada de 2 puntos con extremos casi coincidentes;
  `<path>` con comando no soportado cuyo `d` "parece" casi cerrado), 
  duplicado exacto (incluido con sentido de recorrido invertido), casi-
  duplicado justo debajo/justo arriba del umbral (casos límite calculados
  programáticamente a partir de la tolerancia real, no números
  aproximados), grupo de 3 duplicados en un solo issue, falso positivo de
  duplicados (formas de tamaño distinto no se agrupan), comandos no
  soportados excluidos y contados, determinismo, orden reproducible,
  SVG vacío, salvaguarda de demasiados subpaths.
- `tests/test_path_checker_service.py` (12 tests) — reproducibilidad, SVG
  de entrada corrupto/no-UTF8/no-XML → `InvalidInputSvgError`, límite de
  tamaño de entrada, timeout real (motor inyectable `check_fn`, incluido el
  test de regresión "retorna pronto aunque el hilo siga corriendo"),
  sanitización de contenido peligroso, no muta bytes de entrada, la
  respuesta nunca incluye el SVG.
- `tests/test_check_route.py` (12 tests) — contrato HTTP, determinismo, SVG
  corrupto → 400, tolerancias fuera de rango → 422, params malformados →
  422, entrada demasiado grande → 413, timeout → 504, demasiados subpaths →
  413, la respuesta nunca incluye el SVG.

Archivos (Backend — `backend/Vectify.Api`):
- `Checking/*` (10 archivos, módulo propio y paralelo a
  `Simplification/`/`Vectorization/`, SIN registro/caché/lock — ver
  decisión de diseño más abajo): `CheckSourceKind` (enum `Vector` |
  `Simplification`), `CheckParameters`, `CheckParameterValidationResult`,
  `ICheckParameterValidator`/`CheckParameterValidator` (resuelve
  sourceKind, y cada tolerancia al valor del cliente si viene informado y
  en rango, o al default configurado), `CheckBounds`/`CheckIssue`
  (`OpenPath`/`DuplicatePath`, jerarquía cerrada tipo unión anidada, mismo
  patrón que `SimplificationPreviewResult`)/`CheckDuplicateMember`,
  `ICheckService`/`CheckService` (localiza el SVG de origen vía
  `IVectorizationService.FindVector` o `ISimplificationService.FindSimplification`
  según `SourceKind`, sin duplicar lógica de resolución de storage; llama al
  cliente Python; NUNCA cachea, cada llamada vuelve a analizar),
  `CheckResult` (unión `Ready`/`NotFound`/`ValidationFailed`/`UpstreamError`).
- `Clients/IPythonCheckClient.cs`, `PythonCheckClient.cs`,
  `PythonCheckResult.cs` — cliente tipado dedicado con timeout propio y
  validación defensiva adicional sobre la respuesta (JSON bien formado,
  campos esperados, cada issue con forma coherente según su `type`, el
  resumen `summary` coincide con la cantidad real de issues devueltos,
  tolerancias efectivas en rango, bounds finitos/coherentes) — mismo
  criterio de defensa en profundidad que `PythonSimplifyClient`/
  `PythonVectorizeClient`.
- `Contracts/CheckRequest.cs`, `CheckResponse.cs` (issues polimórficos vía
  `[JsonPolymorphic]`/`[JsonDerivedType]`, discriminador `"type"`:
  `"open_path"` | `"duplicate_path"`), `PythonCheckPayload.cs` (forma cruda
  snake_case, aplanada — un único DTO de entrada con campos nullable para
  ambos tipos de issue, ya que sí es necesario decidir el tipo por el campo
  `type` antes de construir el tipo de dominio correcto).
- `Endpoints/CheckEndpoints.cs` — `POST .../check` (SIEMPRE 200 en éxito,
  nunca 201: no crea nada).
- `Options/CheckOptions.cs` (timeout, defaults y rango permitido de ambas
  tolerancias).
- `Program.cs`, `appsettings.json` (mod) — wiring de DI/HttpClient y
  sección `Check`.
- Tests: `Checking/FakeVectorizationService.cs`,
  `Checking/FakeSimplificationService.cs`, `Checking/FakePythonCheckClient.cs`,
  `Checking/CheckParameterValidatorTests.cs` (13 tests),
  `Checking/CheckServiceTests.cs` (11 tests: resuelve vector o
  simplificación según sourceKind, nunca persiste nada, nunca cachea entre
  llamadas, errores upstream), `Clients/PythonCheckClientTests.cs` (16
  tests: éxito, todos los estados de error, y la validación defensiva
  adicional — issues con forma incoherente, resumen que no coincide con la
  cantidad real de issues, severidad/tipo desconocidos, tolerancias fuera
  de rango), `EndToEnd/CheckEndpointsTests.cs` (8 tests: pipeline completo
  upload→...→vector→check, también sobre una simplificación ya aplicada,
  siempre 200 sin persistir, determinismo, sin caché entre llamadas,
  errores controlados), `TestSupport/CheckPayloads.cs`,
  `TestSupport/FakePythonPreprocessServer.cs` (mod: agrega ruta
  `/api/v1/check`).

Archivos (Frontend — `frontend/src`):
- `types/check.ts`, `api/checkApi.ts` — `runPathCheck` (POST .../check, a
  pedido, nunca automático) y `fetchSvgText` (GET del mismo `svgUrl` que ya
  usa `<img>`, pero leído como texto para poder resaltar un path
  localmente antes de renderizarlo).
- `hooks/useCheck.ts` — estados idle/running/ready/error; `run()` dispara a
  pedido (nunca automático); cambiar de fuente (sourceKind/sourceId)
  invalida el resultado vigente y exige volver a pedir el análisis.
- `lib/highlightSvgPath.ts` (+ test, 7 casos) — inyecta atributos de
  PRESENTACIÓN directos (`stroke`/`fill`/`fill-opacity`, no una clase CSS +
  `<style>`: VectorCanvas renderiza como `<img>`, que no aplica hojas de
  estilo externas, y `<style>` es uno de los elementos que la sanitización
  del lado Python elimina) sobre uno o más `<path>` por índice de
  documento, vía DOMParser/XMLSerializer en memoria — nunca
  `dangerouslySetInnerHTML`, mismo criterio de defensa en profundidad que
  el resto del visualizador (M1-S06/M1-S07): el resultado se convierte a
  data URL y se pasa como `src` de un `<img>`. Fail-safe: nunca lanza, ante
  cualquier SVG malformado o índice inexistente devuelve el texto de
  entrada intacto.
- `components/check/CheckIssueList.tsx` — panel de issues con severidad
  (badge Error/Advertencia), filtro por tipo (Todos/Abiertos/Duplicados,
  radiogroup nativo) y cada issue como `<button>` (nunca un `<div>` con
  onClick) para click-to-highlight.
- `components/check/CheckPanel.tsx` (+ test, 9 casos) — orquesta selección
  de fuente (vector actual / última simplificación, si existe) → "Analizar"
  → contadores + lista de issues → click-to-highlight sobre un
  `VectorCanvas` reutilizado de M1-S06 (con su propio toolbar de zoom/pan/
  fit-to-screen, mismo patrón que `SimplifyComparison`). El texto del SVG
  se descarga una única vez al quedar listo el análisis (no en cada click
  de issue).
- `components/simplify/SimplifyPanel.tsx` (mod) — nuevo prop opcional
  `onSimplificationApplied`, mismo criterio que `VectorizePanel.onVectorReady`,
  para que el Laser Checker pueda ofrecer la simplificación aplicada como
  fuente alternativa.
- `App.tsx`, `App.css` (mod) — encadena la sección del Laser Checker tras
  un vector listo; construye las fuentes disponibles (vector + simplificación
  opcional) para `CheckPanel`.

Dependencias agregadas: ninguna (Union-Find y comparación geométrica
implementados a mano en Python puro; DOMParser/XMLSerializer del lado
frontend son APIs nativas del navegador/jsdom, no una librería nueva).

Verificación (corrida de verdad):
- Backend: `dotnet build` → 0 errores, 0 warnings. `dotnet test` → **279/279
  pasaron** (231 base + 48 nuevos: 13 validador + 11 servicio + 16 cliente
  Python + 8 end-to-end).
- Python: `pytest` → **211/211 pasaron** (169 base + 42 nuevos: 18 core +
  12 servicio + 12 ruta HTTP).
- Frontend: `tsc -b && vite build` → OK. `oxlint` → sin hallazgos. `npm test`
  (vitest) → **84/84 pasaron** (68 base + 16 nuevos: 7 `highlightSvgPath` +
  9 `CheckPanel`).

## Decisiones de diseño y supuestos

- **Dónde vive el análisis geométrico: Python, no C#** (aunque el cuerpo de
  la tarjeta dice "ASP.NET Core/Vector: servicio de validación... resultados
  tipados"). Se interpretó eso como "ASP.NET Core expone el servicio/
  contrato tipado hacia React", no "el algoritmo geométrico corre en C#".
  Razón: M1-S07 ya dejó tokenización de paths SVG (M/L/Z) probada y con dos
  bugs de robustez ya corregidos del lado Python (`simplification_pipeline.py`);
  reimplementar un segundo parser de paths SVG en C# hubiera significado
  mantener DOS tokenizers del mismo formato en dos lenguajes, con riesgo
  real de que diverjan (ya pasó una vez dentro del mismo módulo Python).
  ASP.NET Core (`Vectify.Api.Checking`) orquesta: localiza el SVG de origen
  (`IVectorizationService`/`ISimplificationService`, sin duplicar lógica de
  resolución de storage), valida/resuelve parámetros, llama al cliente
  Python tipado (`IPythonCheckClient`, con la misma defensa en profundidad
  que el resto de los clientes Python) y tipa la respuesta hacia React
  (`CheckResponse`/`CheckIssuePayload` polimórfico) — exactamente el mismo
  reparto de responsabilidades que Simplificación (M1-S07): "Python calcula,
  .NET orquesta y valida en profundidad, nunca confía ciegamente en su
  caller".
- **Sin caché/lock/registro versionado, a propósito (YAGNI)**: a diferencia
  de Preprocessing/Threshold/Vectorization/Simplification, este checker es
  de SOLO LECTURA y no persiste nada nuevo — no hay un "resultado" que
  versionar (no genera un SVG nuevo ni modifica el existente). Es
  determinista por construcción (mismos bytes + mismas tolerancias = mismo
  resultado), así que no hace falta cachear para garantizar esa propiedad;
  agregar un registro persistente habría sido una abstracción sin necesidad
  real. Cada corrida de "Analizar" vuelve a llamar a Python (verificado en
  tests: `CheckServiceTests.AnalyzeAsync_WhenCalledTwice_CallsPythonTwiceAndNeverCaches`,
  `CheckEndpointsTests.PostCheck_CalledTwice_NeverCachesAndAlwaysCallsPythonAgain`).
- **Origen del SVG: vector O simplificación, decisión explícita del
  caller** — el spec no aclara si el checker opera sobre una `VectorVersion`
  (M1-S05) o una `SimplificationVersion` (M1-S07). Se aceptan AMBAS: el
  request trae `sourceKind` ("vector" | "simplification") + `sourceId`, y
  `CheckService` resuelve el storage key correspondiente reutilizando
  `IVectorizationService.FindVector`/`ISimplificationService.FindSimplification`
  ya existentes (ambos exponen la misma forma `SvgStorageKey`/`ContentType`).
  En React, `CheckPanel` ofrece un selector solo cuando existe una
  simplificación aplicada en la sesión (si no, el vector es la única
  opción); por defecto se preselecciona la fuente más refinada disponible.
- **Criterio geométrico de "path que debería estar cerrado"**: un subpath
  SIN comando `Z` explícito, de al menos 3 puntos (menos de eso es
  degenerado — una línea de 2 puntos o un punto no define un área que tenga
  sentido "cerrar"; ver el test de falso positivo conocido), cuyo primer y
  último punto están a una distancia ≤ `close_gap_ratio × diagonal del
  bounding box de TODO el SVG`. Tolerancia relativa (no absoluta), mismo
  criterio de escalado que el epsilon de Douglas-Peucker de M1-S07.
- **Criterio geométrico de "casi-duplicado"**: dos subpaths con la MISMA
  cantidad de puntos y el MISMO estado de cierre, cuyos puntos — comparados
  índice a índice en el mismo sentido de recorrido o invertido (para
  cubrir agujeros/contornos que VTracer a veces emite con sentido opuesto)
  — están TODOS a una distancia ≤ `duplicate_point_ratio × diagonal`. Los
  subpaths que matchean (directa o transitivamente, vía Union-Find) forman
  UN único issue con todos sus miembros, no un issue por par — evita
  explosión combinatoria de issues cuando una misma forma se repite 3+
  veces. Limitación conocida y documentada: el clustering usa cierre
  transitivo (si A≈B y B≈C pero A y C exceden la tolerancia entre sí, igual
  se agrupan); `max_point_distance` reportado es el máximo real entre
  cualquier par del clúster, así que puede superar levemente la tolerancia
  nominal en casos encadenados — aceptable porque los duplicados reales
  suelen ser copias directas, no cadenas de casi-duplicados.
- **Valores de tolerancia por defecto (spec.md no los cuantifica, supuesto
  documentado)**: `close_gap_ratio` = 0.5% de la diagonal del SVG (un gap
  moderado puede seguir siendo una decisión de diseño legítima — ej. una
  forma tipo "C" con abertura deliberada — así que se prefiere un umbral
  conservador que no dispare de más), `duplicate_point_ratio` = 0.2% de la
  diagonal (más estricto: dos subpaths casi idénticos dentro de ese margen
  son una señal mucho más fuerte de un problema real que un gap de cierre
  moderado). Configurables por request (`CloseGapRatio`/`DuplicatePointRatio`
  opcionales) y por configuración (`Vectify.Api.Options.CheckOptions`,
  sección `Check`), mismo rango permitido `(0, 0.5]` que valida
  `CheckParams` del lado Python.
- **Niveles de severidad (spec.md no los define, supuesto documentado)**:
  `open_path` siempre `"error"` (un path que debería cerrarse y no lo está
  produce un corte incompleto: el resultado de fabricación está mal, no es
  ambiguo). `duplicate_path` es `"error"` si es un duplicado EXACTO
  (distancia máxima punto a punto ≈ 0 — corte redundante completo,
  desperdicio de tiempo/material y riesgo real de sobre-quemado en un
  segundo pase del láser) o `"warning"` si es casi-duplicado pero no exacto
  (podría ser una decisión de diseño deliberada, aunque la tolerancia por
  defecto es lo bastante chica como para que sea poco probable).
- **Salvaguarda de rendimiento adicional al timeout**: `max_check_subpaths`
  (20 000, configurable) rechaza con `TooManySubpathsError`/413 antes
  siquiera de intentar la detección de duplicados (O(n²) sobre la cantidad
  de subpaths analizables) — protección explícita más allá del límite de
  tamaño de bytes ya heredado de M1-S07 (`max_svg_output_bytes`), porque el
  costo de este análisis específico escala con la cantidad de subpaths, no
  directamente con el tamaño en bytes del archivo.
- **Extracción de `app.core.svg_path_parsing`**: se evaluó no tocar
  `simplification_pipeline.py` y simplemente copiar/pegar el tokenizer
  dentro de `path_checker.py` (menor riesgo de romper M1-S07), pero se
  decidió extraer a un módulo compartido — el propio enunciado de la
  tarjeta señala este riesgo explícitamente ("evaluá si conviene moverlas a
  un módulo común... para no duplicar"), y ya hay precedente concreto de que
  dos copias del mismo tokenizer divergen con el tiempo (los dos bugs
  corregidos en M1-S07). El refactor se verificó sin cambiar comportamiento:
  los 13 tests de `test_simplification_pipeline.py` pasan sin modificarlos.
- **Resaltado en el frontend sin reintroducir SVG inline**: dado que
  `VectorCanvas` renderiza el SVG como `<img src="...">` (nunca
  `dangerouslySetInnerHTML`, defensa en profundidad establecida en M1-S06),
  no alcanza con inyectar una clase CSS + `<style>` para resaltar un path
  (un `<img>` no aplica hojas de estilo externas al contenido rasterizado
  que referencia, y además `<style>` es uno de los elementos que la
  sanitización del lado Python elimina por completo). Se optó por inyectar
  atributos de PRESENTACIÓN directos (`stroke`, `stroke-width`, `fill`,
  `fill-opacity`) sobre el/los `<path>` correspondiente(s), vía
  DOMParser/XMLSerializer operando puramente en memoria (nunca adjuntado al
  DOM real), y convertir el resultado a data URL antes de pasarlo como
  `src` — mismo nivel de seguridad que el resto del pipeline, cero SVG vivo
  en el documento.

## Excepciones/limitaciones conocidas

- Heredado de tarjetas anteriores: sin verificación en `docker compose up
  --build` ni navegador real (fuera del ciclo local de build/test).
- Autocorrección, bridges, kerf y validación completa de fabricación: fuera
  de alcance explícito de spec.md, no implementados — este checker es
  puramente de diagnóstico/lectura.
- El clustering de duplicados usa cierre transitivo (documentado arriba,
  "Criterio geométrico de casi-duplicado") — puede agrupar un par de
  subpaths cuya distancia directa excede levemente la tolerancia nominal si
  ambos están "encadenados" a través de un tercero. Aceptado como
  limitación conocida, no afecta los casos de prueba obligatorios (duplicado
  exacto, casi-duplicado dentro/fuera de tolerancia, grupo de 3 duplicados
  directos) todos verificados con tests.
- La detección de "casi-duplicado" requiere que ambos subpaths tengan
  EXACTAMENTE la misma cantidad de puntos (no hay remuestreo/interpolación
  para comparar subpaths con distinta densidad de puntos que trazan la
  misma forma). En la práctica esto cubre el caso más común (paths
  duplicados por copia directa de datos, que preservan la cantidad de
  puntos) pero no detectaría dos subpaths visualmente idénticos generados
  por trazados independientes con distinta cantidad de vértices —
  documentado como limitación conocida, no cubierto por los casos de
  prueba obligatorios de spec.md.
- Test observado como intermitente en la corrida completa del frontend
  (`ThresholdPanel.test.tsx`, heredado de M1-S04, no tocado en este sprint):
  falló una vez al correr junto al resto de la suite completa y pasó tanto
  en corridas aisladas como en una segunda corrida completa inmediata — no
  reproducido de forma consistente, no relacionado con ningún archivo de
  este sprint (M1-S08 no toca `ThresholdPanel.tsx` ni su test). Se deja
  documentado en vez de silenciado.
