status: ok

## M1-S11 · Integración y pruebas E2E

Gate de calidad del MVP 1: **no se agregó ninguna feature nueva**, ni se
tocó código de aplicación de M1-S01 a M1-S10 (`git diff --stat` contra
`main` solo toca `README.md`, `tests/README.md` y archivos nuevos bajo
`tests/e2e/`). El trabajo consistió en (1) un dataset de 6 imágenes PNG
raster reales, generadas programáticamente, (2) una automatización E2E que
encadena TODO el pipeline vía HTTP contra la pila real, (3) correrla de
verdad contra backend+Python locales (sin Docker, que queda para el
orquestador) y (4) documentar el resultado real, incluido un hallazgo real
del Laser Checker (M1-S08) que **no se corrigió** por tocar código de un
sprint ya mergeado sin revisión del orquestador.

---

Archivos nuevos:

- `tests/e2e/fixtures/generate_fixtures.py` — genera los 6 PNG con OpenCV +
  NumPy (mismas dependencias ya en `services/python-engine/requirements.txt`,
  sin agregar ninguna librería nueva). Determinista (incluido el fixture de
  ruido, con semilla fija de NumPy). Docstring de cada función documenta el
  razonamiento geométrico completo.
- `tests/e2e/fixtures/logo.png` — anillo + estrella de 5 puntas, negro sobre
  blanco, alto contraste, formas simples.
- `tests/e2e/fixtures/silhouette.png` — blob orgánico (3 círculos solapados
  + blur + threshold), un único contorno cerrado.
- `tests/e2e/fixtures/text.png` — "VECTIFY" renderizado con
  `cv2.putText`/Hershey, para producir múltiples subpaths pequeños.
- `tests/e2e/fixtures/holes.png` — dona (círculo negro con círculo blanco
  concéntrico "perforándolo"), topología con agujero real.
- `tests/e2e/fixtures/noise.png` — círculo sobre fondo, con ruido gaussiano
  fuerte (σ=42) agregado a toda la imagen; semilla fija.
- `tests/e2e/fixtures/problematic.png` — dos círculos negros congruentes en
  posiciones distintas del lienzo, diseñado para disparar el Laser Checker
  (ver "Hallazgo" más abajo sobre por qué esto funciona de forma
  determinista).
- `tests/e2e/full_pipeline_e2e_test.mjs` — automatización E2E (Node, fetch
  nativo, sin dependencias nuevas): para cada fixture, encadena upload →
  preview → threshold → vectorize → simplify(preview+apply) → check →
  dimensions/apply → export vía HTTP real contra la Web API real (asume la
  pila ya arriba, no la levanta — mismo criterio que
  `tests/e2e/real_stack_test.py`/`tests/e2e/smoke_test.py`). Valida
  encadenamiento estructural real entre etapas (mismo `vectorId`/
  `sourceId`/`sourceKind` de punta a punta), buena formación XML del SVG
  final exportado (validador de balanceo de tags propio, sin dependencia
  nueva), dimensiones en mm no degeneradas, y — específicamente para
  `problematic.png` — que el Laser Checker devuelve `openPathCount>0` o
  `duplicateGroupCount>0`. Para `noise.png` corre un paso extra
  (preview+threshold+vectorize con `denoise=0`) para comparar
  `approxNodeCount` con/sin denoise y demostrar que el pipeline de
  reducción de ruido (M1-S03) hace una diferencia real medible. Mide tiempo
  por paso y por fixture, e imprime un reporte legible al final.

Archivos modificados (solo documentación):

- `README.md` — tres bullets nuevos en el resumen de estado (M1-S09/M1-S10,
  que no tenían bullet propio pese a estar implementados y testeados, y
  M1-S11), secciones nuevas "Dimensiones físicas en mm (M1-S09)" y
  "Exportación SVG (M1-S10)" (documentación que faltaba, ver "Hallazgo:
  README con gap de documentación" más abajo), sección nueva "Flujo E2E
  completo (M1-S11)" (dataset, cómo correr la automatización, checklist
  manual), y una línea nueva en "Tests" apuntando al script E2E.
- `tests/README.md` — sección nueva "E2E del pipeline completo (M1-S11)".

Dependencias agregadas: ninguna. `generate_fixtures.py` usa `cv2`/`numpy`,
ya dependencias de producción de `services/python-engine`. El script E2E
usa `fetch`/`FormData`/`Blob` nativos de Node 24 (mismo criterio que
`tests/e2e/upload_e2e_test.mjs`), sin `requests`/`axios`/parsers XML de
terceros — la validación de buena formación XML se implementó a mano
(balanceo de tags vía regex) en vez de sumar una librería solo para esto.

## Verificación (corrida real)

Backend:
```
dotnet build backend/Vectify.sln   → 0 errores, 0 advertencias
dotnet test backend/Vectify.sln    → 402/402 pasaron
```

Python:
```
services/python-engine/.venv/Scripts/python.exe -m pytest
→ 212 passed, 1 warning (warning preexistente de starlette/httpx, no
  introducido por este sprint) en 8.46s
```

Frontend (`frontend/`):
```
npm test -- --run   → 15 test files, 121/121 pasaron
npm run build       → tsc -b && vite build, OK
npm run lint        → oxlint, 0 hallazgos, exit code 0
```

E2E del pipeline completo — corrida real contra backend (`:5080`) y motor
Python (`:8001`) levantados a mano en este entorno, SIN Docker (ver
"Arranque en local" del README: `uvicorn app.main:app --host 127.0.0.1
--port 8001` + `dotnet backend/Vectify.Api/bin/Debug/net9.0/Vectify.Api.dll`
con `PythonEngine__BaseUrl=http://127.0.0.1:8001`), confirmados `online`/
`online` vía `/api/v1/system/health` antes de correr el script:

```
BACKEND_URL=http://127.0.0.1:5080 node tests/e2e/full_pipeline_e2e_test.mjs
```

Resultado: **6/6 fixtures completaron el pipeline entero sin pasos
manuales**, código de salida 0. Reporte real de la segunda corrida (proceso
ya "caliente" — JIT/conexiones ya establecidas por la primera corrida, que
tardó 13990 ms totales por el arranque en frío del primer request a cada
endpoint):

| Fixture | Tiempo total | pathCount | approxNodeCount | reductionPercent | openPathCount | duplicateGroupCount | Export final |
|---|---|---|---|---|---|---|---|
| logo.png | 241 ms | 2 | 97 | 23.7% | 0 | 0 | 100mm × 100.000mm |
| silhouette.png | 110 ms | 1 | 41 | 14.6% | 0 | 0 | 100mm × 100.000mm |
| text.png | 144 ms | 7 | 100 | 15.0% | 0 | 0 | 100mm × 33.333mm |
| holes.png | 110 ms | 1 | 60 | 6.7% | 0 | 0 | 100mm × 100.000mm |
| noise.png | 162 ms | 1 | 31 | 3.2% | 0 | 0 | 100mm × 100.000mm |
| problematic.png | 98 ms | 2 | 40 | 0.0% | 0 | **1** | 100mm × 100.000mm |

Tiempo total de la corrida completa (6 fixtures, proceso caliente): **865 ms**.

Verificación específica de `noise.png` (denoise real, no trivial de
threshold-ear): `approxNodeCount` **612 sin denoise** (denoise=0) vs. **31
con denoise** (denoise=4) — el pipeline de M1-S03 reduce el contorno
resultante en ~95% de nodos, confirmando que el fixture realmente ejercita
el suavizado y no solo produce una máscara ya limpia por casualidad.

Verificación específica de `problematic.png`: el Laser Checker devolvió
`summary.duplicateGroupCount = 1` con un issue `duplicate_path`,
`exact: true` — ver "Hallazgo" abajo para el razonamiento de por qué este
fixture dispara esto de forma determinista.

Ningún assert del script falló en ninguna corrida — cero bugs bloqueantes
encontrados en el pipeline HTTP real de M1-S01 a M1-S10 durante este
sprint.

## Hallazgo: el Laser Checker (M1-S08) ignora el `transform` de cada `<path>`

**No es un bug introducido por este sprint** — es un comportamiento
preexistente de `app.core.path_checker.py` (M1-S08, mergeado), descubierto
al diseñar `problematic.png`. Documentado acá, **no corregido**, porque
toca código de un sprint ya mergeado y el spec de M1-S11 pide explícitamente
"no lo arregles vos mismo... déjalo documentado como hallazgo pendiente de
decisión" salvo que sea trivial — esto no lo es: cambiar la semántica de
comparación de puntos (aplicar o no el offset del `transform`) es una
decisión de producto/diseño, no un typo.

**Qué pasa**: `VtracerEngine` (mode="polygon") emite, para dos formas
congruentes en posiciones distintas del lienzo, dos `<path>` con
coordenadas `d` LOCALES idénticas byte a byte (relativas al origen propio
de cada forma) y solo un `transform="translate(tx,ty)"` distinto por path
— verificado empíricamente:

```
<path d="M0,0 L9,2 L15,6 ... Z " fill="#000000" transform="translate(50,30)"/>
<path d="M0,0 L9,2 L15,6 ... Z " fill="#000000" transform="translate(150,130)"/>
```

`app.core.path_checker._collect_subpaths` (vía `svg_path_parsing.
tokenize_path_d`) lee el atributo `d` crudo y **nunca aplica el
`transform`** del `<path>` que lo contiene (a diferencia de
`app.core.svg_processing.compute_svg_stats`, que sí lo aplica para bounds).
El resultado: dos formas congruentes pero visualmente en posiciones
DISTINTAS del diseño se reportan como `duplicate_path` con `exact: true`
(severidad "error").

**Por qué importa**: en un diseño real de corte láser, es común tener
elementos repetidos congruentes en posiciones distintas (ej. varios
agujeros de tornillo del mismo diámetro, elementos decorativos repetidos).
Con el comportamiento actual, el Laser Checker los marcaría como "path
duplicado" (severidad error) cuando en realidad son dos cortes legítimos y
distintos — un falso positivo real, no cosmético (el summary/severity
"error" podría hacer que un usuario borre o dude de geometría válida).

**Qué NO se tocó**: `services/python-engine/app/core/path_checker.py` y
`services/python-engine/app/core/svg_path_parsing.py` quedan sin cambios.
Se aprovechó este comportamiento, tal como está documentado, para diseñar
`problematic.png` de forma 100% determinista (spec.md M1-S11 sugiere
exactamente este enfoque: "una imagen con dos formas idénticas superpuestas
[...] para duplicados"). Queda a criterio del orquestador si esto amerita
una tarjeta correctiva (aplicar el `transform` antes de comparar puntos,
mismo criterio que `compute_svg_stats`) o si se considera un límite de
alcance aceptable del checker actual.

## Hallazgo: README sin secciones de M1-S09/M1-S10

Al preparar la sección "Flujo E2E completo", se encontró que `README.md`
documentaba M1-S01 a M1-S08 en detalle pero saltaba directo a "Fuera de
alcance" sin ninguna sección para Dimensiones físicas (M1-S09) ni
Exportación SVG (M1-S10), pese a que ambos endpoints están implementados,
testeados (402/402 incluye sus tests) y en uso por el propio pipeline E2E
de esta tarjeta. Se consideró un gap de documentación trivial y bajo riesgo
(puramente aditivo, sin tocar código), así que se agregaron ambas secciones
breves (mismo estilo que las secciones existentes) como parte de dejar el
README "seguible literalmente por un desarrollador nuevo" — Definition of
Done explícita de M1-S11.

## Fuera de mi alcance en esta tarjeta (responsabilidad del orquestador)

Según las instrucciones de esta tarea, explícitamente NO se hizo:

- **`docker compose up --build` real**: no se tocó `docker-compose.yml` ni
  se intentó levantar Docker. El checklist manual en README ("Flujo E2E
  completo → Checklist manual") deja el paso documentado para quien lo
  corra.
- **Verificación en navegador real (Browser pane)**: no se abrió
  http://localhost:5173 para inspección visual manual del flujo completo.
  Mismo checklist manual en README.
- **Prueba de importación en software de fabricación externo** (ej.
  LightBurn): no ejecutable en este entorno (sin el software instalado),
  documentado como checklist manual, consistente con la ambigüedad ya
  resuelta en spec.md ("cuando corresponda").

La automatización E2E (HTTP real, sin navegador) sí se corrió de verdad y
pasó 6/6, cubriendo el criterio de aceptación explícito ("Upload →
procesamiento → vectorización → validación → escala → export funciona E2E
sin pasos manuales internos") de forma verificable y repetible.

## Cómo reproducir esta corrida

```bash
# 1) Generar el dataset (ya versionado en el repo; regenerar es opcional)
services/python-engine/.venv/Scripts/python.exe tests/e2e/fixtures/generate_fixtures.py

# 2) Levantar la pila local (ver README, "Arranque en local (sin Docker)")
cd services/python-engine && .venv/Scripts/python.exe -m uvicorn app.main:app --host 127.0.0.1 --port 8001 &
cd backend/Vectify.Api && dotnet run &

# 3) Correr el E2E completo
BACKEND_URL=http://localhost:5080 node tests/e2e/full_pipeline_e2e_test.mjs
```

---

## Addendum: fix del hallazgo de `transform` en el Laser Checker (M1-S11, corrección post-review)

El orquestador confirmó de forma independiente el hallazgo documentado arriba
("El Laser Checker (M1-S08) ignora el `transform` de cada `<path>`") y
aprobó corregirlo dentro de esta misma tarjeta. Esta sección documenta esa
corrección, aplicada DESPUÉS de la corrida E2E original reportada arriba.

### Fix aplicado

`services/python-engine/app/core/path_checker.py`:

- Nueva función `_parse_translate_transform(transform: str) -> tuple[float, float] | None`,
  con regex `_TRANSLATE_ONLY_RE` que reconoce EXCLUSIVAMENTE
  `translate(tx,ty)` o `translate(tx)` (ty=0 implícito, forma válida en
  SVG) vía `fullmatch` sobre el valor ya recortado de espacios -- el único
  patrón que emite `VtracerEngine` (verificado empíricamente, ver
  `app/core/vector_engine.py`). Devuelve `(0.0, 0.0)` si el atributo está
  ausente/vacío (equivalente a no desplazar, mismo comportamiento que
  antes del fix), y `None` si el valor no calza exactamente con ese
  patrón (rotate/scale/matrix/skew, transforms encadenados, o un
  `translate` con formato que no parsea limpio).
- `_collect_subpaths` ahora llama a esa función para CADA `<path>`; si
  devuelve `None`, el `<path>` completo se excluye del análisis con el
  MISMO criterio que un comando de path no soportado (se suma a
  `skipped_path_count`, nunca se analiza ignorando el transform en
  silencio). Si devuelve un offset válido, ese offset `(tx, ty)` se aplica
  a TODOS los puntos de TODOS los subpaths de ese `<path>` de forma
  UNIFORME -- tanto para la detección de paths abiertos como de
  duplicados (la traslación no cambia el resultado de "abierto" por
  preservar distancias relativas, pero aplicar la corrección de forma
  pareja en vez de tratar los dos análisis distinto es más simple y más
  robusto, tal como pidió la tarjeta correctiva). `_bounds_of` se sigue
  calculando sobre los puntos YA trasladados, así que los `bounds` que
  reciben los issues (y en definitiva React) quedan en coordenadas
  ABSOLUTAS del lienzo -- corrige además la inconsistencia preexistente
  con `app.core.svg_processing.compute_svg_stats` (que ya aplicaba el
  offset para sus propios bounds).
- Docstring del módulo actualizado con una sección nueva ("Resolución de
  `transform`") documentando el criterio exacto y el porqué.

**Decisión de diseño**: la función de parseo de `transform` se agregó
DIRECTAMENTE en `path_checker.py`, NO en `app.core.svg_path_parsing.py`
(el módulo compartido con `simplification_pipeline.py`). Razón: la tarjeta
pidió evaluar con cuidado que no se usara por accidente en
`simplification_pipeline.py` de un modo que cambiara su comportamiento ya
congelado en M1-S07 -- `simplify_svg_paths` nunca necesita resolver
`transform` (deja ese atributo intacto tal cual llega, ver su propio
docstring: "cualquier otro atributo/elemento (fill-rule, transform, orden
de subpaths) queda intacto"), así que agregar la función al módulo
compartido solo hubiera sumado una API sin consumidor real ahí y un
riesgo de acoplamiento futuro no pedido. Mantenerla local a
`path_checker.py` es el cambio de menor superficie que resuelve el bug sin
tocar el contrato ni el comportamiento de M1-S07.

`app.core.svg_path_parsing.py` (el módulo compartido) queda SIN cambios.

### Tests de regresión nuevos (`services/python-engine/tests/test_path_checker.py`)

Cinco tests nuevos, en una sección nueva ("Resolución de
`transform="translate(...)"`"):

1. `test_congruent_shapes_at_different_translated_positions_are_not_duplicates`
   -- reproduce EXACTAMENTE el caso real (mismo `d` local, `transform`
   distinto, posiciones absolutas no superpuestas) -- NO debe reportarse
   como duplicado. Este es el test que reproduce el bug original.
2. `test_same_local_d_and_same_translate_is_a_real_duplicate` -- mismo `d`
   local Y mismo `transform` (duplicado exacto legítimo en la misma
   posición real) -- SÍ debe reportarse (cobertura explícita de un caso
   que ya funcionaba por casualidad antes del fix).
3. `test_different_local_d_that_lands_on_same_absolute_position_is_duplicate`
   -- `d` local DISTINTO que, al aplicar transforms distintos, cae en las
   MISMAS coordenadas absolutas -- ahora SÍ se detecta (antes era un falso
   NEGATIVO, corregido como efecto colateral correcto del fix).
4. `test_unsupported_transform_excludes_the_path_and_counts_as_skipped` --
   `transform="rotate(45)"` -- el `<path>` se excluye por completo
   (`skipped_path_count`), nunca se analiza ignorando el transform.
5. `test_path_without_transform_attribute_is_analyzed_as_before` -- sin
   atributo `transform` -- se sigue analizando igual que antes del fix
   (equivalente a `translate(0,0)`).

### Resultado de pytest (corrida real, después del fix)

```
services/python-engine/.venv/Scripts/python.exe -m pytest -q
→ 217 passed, 1 warning (mismo warning preexistente de starlette/httpx)
  en 4.03s
```

212 tests previos + 5 tests nuevos de este fix = 217. Sigue en verde.

### Ajuste del fixture `problematic.png` y re-corrida del E2E

Con el fix aplicado, `problematic.png` (dos círculos congruentes en
posiciones REALES distintas) YA NO dispara `duplicate_path` -- ese era
justamente el falso positivo que se corrigió. Se evaluó regenerar el
fixture para que las dos formas quedaran en la MISMA posición real (mismo
`transform`) y así seguir disparando un duplicado, ahora LEGÍTIMO -- pero
se encontró, y se verificó EMPÍRICAMENTE, que esto es geométricamente
IMPOSIBLE de lograr vía trazado raster:

- Un "duplicado" solo se reporta cuando los puntos ABSOLUTOS de dos
  subpaths coinciden dentro de tolerancia -- eso significa, por
  definición, que ambas formas ocupan literalmente la misma región del
  lienzo.
- Dos regiones rellenas del MISMO color que ocupan literalmente la misma
  posición real en un raster plano se FUNDEN en una única región al
  trazar (VTracer no "apila" instancias -- traza regiones conectadas
  únicas). Se verificó dibujando el mismo círculo dos veces en la misma
  posición: el resultado es un único `<path>`, nunca dos (ver
  `services/python-engine/tests/e2e` -- probado ad-hoc contra
  `VtracerEngine` real, no incluido como fixture por ser un experimento de
  verificación, no un caso de uso).
- Por lo tanto, un duplicado "legítimo" (misma posición real, dos
  elementos SEPARADOS) solo puede existir en un SVG de autoría manual, no
  en la salida de `VtracerEngine` sobre un PNG plano de un solo color de
  relleno.

Dado esto (y que spec.md M1-S11 pide un fixture que dispare "paths
abiertos Y/O duplicados", no específicamente duplicados), se decidió:

- **No regenerar los píxeles de `problematic.png`** (serían bytes
  idénticos de todas formas -- confirmado corriendo
  `generate_fixtures.py` de nuevo: mismo SHA-256 antes/después de este
  fix, porque el fix no cambia la geometría del fixture, solo la
  interpretación que hace el checker).
- **Repurpuar el fixture como prueba de regresión del fix**: se actualizó
  el docstring de `make_problematic()` en
  `tests/e2e/fixtures/generate_fixtures.py`, el comentario/aserciones de
  `tests/e2e/full_pipeline_e2e_test.mjs` (ahora exige
  `duplicateGroupCount === 0`, `openPathCount === 0`, `issues.length ===
  0` y `pathCount >= 2` para este fixture, en vez de exigir al menos un
  issue), y las menciones en `README.md`/`tests/README.md`.
- **El escenario de duplicado LEGÍTIMO (misma posición real) queda
  cubierto exhaustivamente a nivel unitario**, no en el dataset raster --
  ver tests 2 y 3 de la lista de arriba en
  `services/python-engine/tests/test_path_checker.py`.

### Resultado de la re-corrida del E2E completo (corrida real, después del fix)

La pila Docker del orquestador (`vectify-backend-1`, `vectify-python-engine-1`,
`vectify-frontend-1`, en `:5080`/`:8001`/`:5173`) ya estaba arriba durante
esta corrección (imagen construida ANTES de este fix) -- para no
interferir con ella, se levantó una pila LOCAL adicional, sin Docker, en
puertos distintos (motor Python en `:8002`, backend en `:5081` con
`PythonEngine__BaseUrl=http://127.0.0.1:8002`), se corrió el E2E contra
esa pila, y luego se detuvieron esos dos procesos locales, dejando la pila
Docker del orquestador intacta y corriendo (verificado con un `curl` a
`:8001/health` después de detener los procesos locales):

```
BACKEND_URL=http://127.0.0.1:5081 node tests/e2e/full_pipeline_e2e_test.mjs
```

Resultado: **6/6 fixtures pasaron**, código de salida 0.

| Fixture | pathCount | openPathCount | duplicateGroupCount | Export final |
|---|---|---|---|---|
| logo.png | 2 | 0 | 0 | 100mm × 100.000mm |
| silhouette.png | 1 | 0 | 0 | 100mm × 100.000mm |
| text.png | 7 | 0 | 0 | 100mm × 33.333mm |
| holes.png | 1 | 0 | 0 | 100mm × 100.000mm |
| noise.png | 1 | 0 | 0 | 100mm × 100.000mm |
| problematic.png | 2 | 0 | **0** | 100mm × 100.000mm |

`problematic.png` antes de este fix reportaba `duplicateGroupCount = 1`
(el falso positivo); en esta corrida, con el fix aplicado, reporta
`duplicateGroupCount = 0` -- confirmación E2E real, contra la pila HTTP
completa (no solo a nivel unitario), de que el fix resuelve el problema
de punta a punta. `pathCount = 2` confirma que las dos formas congruentes
se siguen vectorizando como dos `<path>` separados (el escenario se sigue
ejercitando de verdad, solo que ahora el checker lo interpreta
correctamente).

`noise.png` repitió la verificación de denoise real: 612 nodos sin
denoise vs. 31 con denoise, igual que en la corrida original.

### Archivos modificados por este fix (además de los ya listados arriba)

- `services/python-engine/app/core/path_checker.py` -- fix (ver arriba).
- `services/python-engine/tests/test_path_checker.py` -- 5 tests nuevos.
- `tests/e2e/fixtures/generate_fixtures.py` -- docstring de
  `make_problematic()` actualizado (sin cambio de píxeles, mismo SHA-256).
- `tests/e2e/full_pipeline_e2e_test.mjs` -- comentario de cabecera,
  label del fixture y aserciones del caso `problematic.png` actualizados
  para reflejar el comportamiento correcto post-fix.
- `README.md` / `tests/README.md` -- referencias a `problematic.png` y al
  comportamiento del Laser Checker actualizadas para no describir un bug
  ya corregido como si fuera el comportamiento actual.

Ningún archivo de `.NET` ni de `frontend/` fue tocado por este fix --
consistente con lo pedido: el bug estaba encapsulado enteramente en el
motor Python y el contrato de `analyze_svg_paths(sanitized_svg,
close_gap_ratio, duplicate_point_ratio, max_subpaths) -> dict` no cambió.

## Verificación del orquestador (Docker + navegador real)

Ejecutado personalmente, por primera vez en todo el proyecto (excepción
heredada desde M1-S01, y criterio de salida explícito de esta tarjeta):

**Docker**: `docker compose up --build` (sin `.env`, los defaults de
`docker-compose.yml` alcanzan) construyó y arrancó los 3 servicios
correctamente. Verificado: `/health` de los tres, `/api/v1/system/health`
en `online`, degradación a `degraded` tras `docker compose stop
python-engine` (la Web API sigue respondiendo, nunca se cae) y recuperación
a `online` tras `docker compose start python-engine`.

**Navegador real**: la UI en `:5173` carga y muestra "Todos los servicios
están en línea". Reproduje el bug de `transform` yo mismo, de forma
independiente al hallazgo del implementador, corriendo el pipeline completo
vía HTTP real contra el stack de Docker con `logo.png` (upload → preview →
threshold → vectorize → simplify → dimensions → export), y **visualicé el
SVG final exportado en una pestaña real** (renderiza correctamente). El
navegador automatizado disponible no puede completar un `<input
type="file">` real (bloqueo de seguridad del navegador, no del código de la
app) -- lo dejé documentado como único paso genuinamente manual en el
checklist del README.

**Re-verificación del fix contra Docker real**: al correr
`full_pipeline_e2e_test.mjs` contra el stack de Docker (puerto `:5080`)
DESPUÉS del fix, `problematic.png` seguía fallando con
`duplicateGroupCount=1` -- no una regresión, sino que la imagen de
`python-engine` en Docker era la construida ANTES del fix (el implementador
había verificado el fix contra una pila local separada, sin Docker, para no
interferir con el stack que yo tenía arriba). Reconstruí solo ese servicio
(`docker compose up --build python-engine`) y volví a correr el E2E
completo contra el contenedor ya actualizado: **6/6 fixtures, incluido
`problematic.png` con `duplicateGroupCount=0`** -- confirmación de que el
fix funciona de punta a punta contra la imagen Docker real, no solo contra
un venv local.

**Nota sobre el dataset y la detección positiva a nivel E2E**: confirmé
independientemente (corriendo los 6 fixtures contra `analyze_svg_paths`
directamente) que, con el fix aplicado, NINGUNO de los 6 fixtures dispara
un issue del Laser Checker -- ni siquiera `problematic.png`, que ahora
correctamente no debería. Esto significa que el dataset E2E, tal como
queda, nunca ejercita el camino de detección POSITIVA del checker a nivel
de integración (solo el camino "no encuentra nada", que también es
válido). Acepto el razonamiento documentado arriba por el implementador:
es geométricamente imposible producir un duplicado legítimo (misma
posición real) vía trazado raster con VTracer (dos formas superpuestas se
funden en un único `<path>`), y un "path abierto" tampoco es alcanzable de
forma natural porque VTracer en `mode="polygon"` siempre cierra los
contornos que traza. El caso positivo de detección queda cubierto a nivel
unitario en `test_path_checker.py` (5 tests nuevos + los ya existentes de
M1-S08), que es una cobertura más rigurosa y determinista que depender de
que un fixture raster específico dispare el comportamiento por casualidad.
No lo considero un bloqueante para esta tarjeta, pero lo documento
explícitamente como limitación conocida: el Laser Checker, en el estado
actual del pipeline (sin edición manual de nodos, MVP2/MVP3), puede no
tener ningún escenario realista donde efectivamente encuentre algo que
reportar -- su valor se vuelve más relevante recién cuando MVP2/MVP3
agreguen edición manual de paths.

Verificado también: `dotnet test` 402/402, `npm test` 121/121 (sin cambios
en esos árboles, corridos para confirmar ausencia de regresión).
