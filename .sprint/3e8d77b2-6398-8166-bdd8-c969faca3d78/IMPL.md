status: ok

## M2.1-S01 — Auditoría y corrección del pipeline multicolor

### 1. Causa raíz — confirmada independientemente (no solo citando al orquestador)

Antes de tocar código, reproduje el pipeline completo contra el stack Docker REAL (los mismos contenedores `vectify-backend-1`/`vectify-python-engine-1` que ya corrían, build de `main` previo a este fix) con un dataset propio, generado con OpenCV dentro del propio contenedor `python-engine` (mismo cv2 que usa el pipeline real), sin reutilizar el fixture de 5 colores del orquestador:

- `01_logo_bn_2colores.png` — cuadrado negro sólido sobre blanco (2 colores, sin antialiasing).
- `02_bloques_rgby_fondo_blanco_5colores.png` — 4 bloques RGBY + fondo blanco (5 colores).
- `03_exactamente_4_colores.png` — 4 cuadrantes, EXACTAMENTE 4 colores, sin fondo (fixture del DoD).
- `04_6colores_grid.png` — grid 3x2, 6 colores, sin fondo.
- `05_ilustracion_infantil_solida.png` — "ilustración infantil" (cielo, pasto, sol, casa, puerta), 5 colores sólidos, sin antialiasing.
- `06_ilustracion_con_antialiasing.png` — misma ilustración con `cv2.LINE_AA` en el sol y un techo triangular.
- `07_png_transparencia.png` — círculo rojo opaco + anillo rojo semi-transparente sobre fondo totalmente transparente.

Recorrí manualmente, vía `curl` contra `http://localhost:5080`, el flujo real `POST /projects` → `POST .../color-palette/detect` → `POST .../confirm` → `POST .../layers` → `GET .../vectors/{vectorId}` para cada fixture. Resultado (idéntico al hallazgo ya confirmado por el orquestador, ahora verificado con mi propio dataset):

- La detección de paleta (M2-S01) es correcta en TODOS los casos: `colorHex` exacto, `groupId`/`paletteId` estables, `hasPartialAlpha`/`transparentPercent` coherentes (ver `01_before_4colores_detect.json`, `05_before_2colores_detect.json`, `09_before_transparencia_detect.json` en `evidence/`).
- El SVG real de CADA capa, para TODOS los fixtures (2, 4, 5 y 6 colores; con y sin antialiasing; con transparencia), sale con `fill="#000000"` sin importar el `colorHex` real del grupo. Único caso "invisible" a simple vista: en el fixture de 2 colores, el grupo negro (`#000000`) coincide por casualidad con el fill fijo del motor — pero el grupo blanco (`#ffffff`) también sale negro (ver `05_before_2colores_layer1_white.svg`), confirmando que el bug aplica igual, solo que uno de los dos casos lo enmascara.

Inspeccioné directamente el código (no until confié en la cita del orquestador):

- `services/python-engine/app/core/vector_engine.py` (`VtracerEngine.trace`): llama a `vtracer.convert_raw_image_to_svg(..., colormode="binary", ...)`. Confirmé en el propio docstring de la clase y en el código que este modo no recibe ni conoce ningún color de entrada — la wheel de VTracer en `colormode="binary"` siempre emite un fill fijo.
- `services/python-engine/app/services/vectorization_service.py` (`VectorizationService.process`): re-binariza la máscara (0/255) y llama a `self._engine.trace(mask)` — nunca recibe ni pasa un color.
- `services/python-engine/app/api/routes/vectorize.py` (`vectorize_layers`): por cada máscara llama `service.process(data)` (el MISMO `VectorizationService` de M1-S05) — confirma que M2-S02 reutiliza el motor compartido tal cual, sin ninguna inyección de color en el camino Python.
- `backend/Vectorify.Api/VectorLayers/VectorLayerService.cs` (`GenerateLayersAsync`, antes del fix): tomaba `layerResult.Svg` (la respuesta cruda de Python) y la persistía **tal cual**, sin ninguna transformación — el `ColorGroup.ColorHex` (`backend/Vectorify.Api/ColorPalette/ColorGroup.cs`) solo se usaba para construir el `VectorLayer` de metadata (línea `layers.Add(new VectorLayer(group.GroupId, group.Name, group.ColorHex, ...))`), nunca para tocar el SVG.

**Conclusión igual a la del orquestador, confirmada con evidencia propia:** la causa raíz es puntual — el color real nunca se aplica a la geometría SVG, solo sobrevive en la metadata JSON. No hay ningún threshold binario "colándose" en la ruta multicolor: confirmé leyendo `app/core/color_palette_pipeline.py` (`detect_palette`) que el clustering de color opera directamente sobre la imagen BGR original (nunca sobre una máscara de `threshold_pipeline`), y que las máscaras que después vectoriza `vectorize_layers` son las máscaras propias de M2-S01 (clustering Lab), no máscaras de threshold B/N.

**Diferencia encontrada y documentada (no bloqueante, fuera del alcance de este fix):** con antialiasing y los parámetros de tolerancia por defecto, `detect_palette` genera muchos grupos extra de "colores casi iguales" en los bordes suavizados (17 grupos en vez de los ~5 lógicos, ver `11_before_antialiasing_detect.json`). Varios de esos grupos son tan pequeños que VTracer (`mode="polygon"`) devuelve un SVG sin ningún `<path>` (ver `13_antialiasing_tiny_group_empty_path_before.svg` / `_after.svg`, ambos vacíos — este comportamiento NO cambia con el fix, porque no hay ningún `<path>` al que aplicarle `fill`). Es una limitación preexistente de VTracer sobre regiones minúsculas, no relacionada con el bug de color — no se tocó, documentado acá para que quede registrado.

### 2. Corrección aplicada

Se aplicó en **.NET**, siguiendo la recomendación del orquestador (mismo patrón que `SvgDimensionWriter` de M1-S09): reescribir el XML del SVG ya recibido de Python, sin tocar el contrato HTTP de `/vectorize-layers` ni `VtracerEngine`/`VectorizationService` compartidos.

**Archivos nuevos:**
- `backend/Vectorify.Api/VectorLayers/SvgFillWriter.cs` — parsea el SVG con `XDocument`, ubica TODOS los `<path>` (a cualquier profundidad, por si VTracer alguna vez los anida en un `<g>`) y les setea `fill="{ColorHex real}"`. Nunca toca `d`, `transform`, `fill-rule` ni ningún otro atributo. Un `<path>` con subpaths anidados (agujeros, `hierarchical="stacked"`) recibe el fill UNA sola vez, sobre el `<path>` completo — el `d` con sus múltiples subpaths (`M...Z M...Z`) no se parte ni se toca.
- `backend/Vectorify.Api/VectorLayers/InvalidLayerSvgException.cs` — excepción de defensa en profundidad si el SVG (ya validado antes por `PythonVectorLayerClient`) no fuera XML válido.

**Archivo modificado:**
- `backend/Vectorify.Api/VectorLayers/VectorLayerService.cs` (`GenerateLayersAsync`): justo antes de persistir cada SVG, se llama `SvgFillWriter.Apply(layerResult.Svg, group.ColorHex)` y se persiste el resultado en vez del SVG crudo. Se agregó logging (`LogInformation`, "Capa {GroupId} pintada con fill {ColorHex} para paleta {PaletteId} de {ProjectId}/{ImageId}") — sin loggear contenido de imágenes/SVG. Si el SVG fuera inválido (no debería pasar, `PythonVectorLayerClient` ya lo valida), se devuelve `UpstreamError("invalid_response", ...)` en vez de propagar una excepción sin manejar.

**Por qué en .NET y no en Python:** mantiene `VtracerEngine`/`VectorizationService` (compartidos con el pipeline B/N de M1-S05) totalmente intactos — cero riesgo de que un cambio "para pintar de color" afecte, aunque sea indirectamente, al flujo B/N. El contrato HTTP `/vectorize-layers` de Python no cambia. El fix queda 100% aislado a `VectorLayerService` (M2-S02), aditivo.

### 3. Evidencia before/after (contenido real, no descripción)

Todos los artefactos completos están en `.sprint/3e8d77b2-6398-8166-bdd8-c969faca3d78/evidence/` (paleta detectada JSON + SVG de cada capa, antes y después, para 2, 4, 6 colores, antialiasing y transparencia). Resumen del caso central (fixture de 4 colores, `03_exactamente_4_colores.png`, replica el DoD "un fixture de 4 colores debe devolver al menos 4 grupos/capas coherentes, cada una con su color real aplicado al SVG"):

Paleta detectada (`01_before_4colores_detect.json`, igual en `02_after_4colores_detect.json` salvo IDs): 4 grupos — `#ff0000` (25%), `#00c800` (25%), `#ffdc00` (25%), `#0000ff` (25%).

SVG de la capa "Color 1" (`#ff0000`) — **ANTES** (`evidence/03_before_4colores_layer1_red.svg`):
```xml
<svg xmlns="http://www.w3.org/2000/svg" version="1.1" width="200" height="200">
<path d="M0,0 L100,0 L100,100 L0,100 Z " fill="#000000" transform="translate(0,0)" />
</svg>
```

**DESPUÉS** (`evidence/04_after_4colores_layer1_red.svg`), mismo `d`/`transform`, único cambio el `fill`:
```xml
<svg xmlns="http://www.w3.org/2000/svg" version="1.1" width="200" height="200">
<path d="M0,0 L100,0 L100,100 L0,100 Z " fill="#ff0000" transform="translate(0,0)" />
</svg>
```

Las otras 3 capas del mismo fixture, después del fix: `#00c800`, `#ffdc00`, `#0000ff` (ver `04_after_4colores_layer{2,3,4}_*.svg`) — 4 colores distintos, ninguno negro.

Caso con agujero/subpaths anidados + transparencia (`07_png_transparencia.png`, círculo con anillo semi-transparente, `hasPartialAlpha=true`): **ANTES** (`evidence/09_before_transparencia_layer1.svg`) dos `<path>` (uno con dos subpaths `M...Z M...Z` para el agujero del anillo), ambos `fill="#000000"`. **DESPUÉS** (`evidence/10_after_transparencia_layer1.svg`) mismos `d` exactos, ambos `fill="#ff0000"` — confirma que el fix pinta TODOS los `<path>` de una capa (no solo el primero) y que un `<path>` con subpaths anidados recibe un único fill sin partir su `d`.

Caso B/N de 2 colores (`01_logo_bn_2colores.png`): **ANTES**, ambas capas (blanco y negro) salían `fill="#000000"` (`evidence/05_before_2colores_layer1_white.svg`). **DESPUÉS**, la capa blanca sale `fill="#ffffff"` y la negra `fill="#000000"` (coincide con el color real, no por el bug) — ver `evidence/06_after_2colores_layer{1,2}_*.svg`.

Reproducido igual (todas las capas con path pasan de negro fijo a su color real) para 6 colores (`evidence/07_before_6colores_detect.json`/`08_after_6colores_detect.json`) y para el caso con antialiasing (`evidence/11_before_antialiasing_layer1.svg` → `evidence/12_after_antialiasing_layer1.svg`).

Telemetría real (logs del contenedor `vectify-backend-1` tras el fix, un ejemplo real):
```
Capa 3e25aac8-db58-465e-9c4b-eae63d3f96e1 pintada con fill #96a3ba para paleta 24456fa9-c57a-4ab1-88a0-697c16c9f5b7 de 5c075358-70a1-418d-b0b2-cc438b51667b/fd863321-331f-43ac-9194-b2353e4f1eda
```
(sin contenido de imagen, solo IDs y el color aplicado).

### 4. Cobertura del "Trabajo requerido" de spec.md

- **Reproducir formalmente con el dataset mínimo completo** — hecho, dataset propio de 7 fixtures (2/4/5/6 colores + los 5 casos explícitos), generado con OpenCV dentro del contenedor real, contra el stack Docker real corriendo (no mocks). Confirmé independientemente el hallazgo del orquestador y documenté la diferencia de antialiasing (grupos extra + algunos SVG sin `<path>`, preexistente y fuera de alcance).
- **Registrar artefactos intermedios** — guardados en `evidence/`: paleta detectada (JSON), SVG por color antes/después. (Las máscaras PNG por color no se adjuntaron -- son binarias y su contenido ya está representado por los `colorHex`/`areaPercent`/`maskUrl` en el JSON de paleta; el documento compuesto/preview no se descargó aparte porque no participa de este bug -- el preview usa la metadata directamente, nunca el SVG vectorizado.)
- **Revisar contratos React → ASP.NET → Python** — revisado explícitamente: `frontend/src/api/vectorLayersApi.ts` (`getVectorLayerSvgUrl`) solo arma una URL, no envía ningún parámetro de color; `Vectorify.Api/Clients/PythonVectorLayerClient.cs` no envía ni recibe ningún parámetro de "modo color/binario" propio; `services/python-engine/app/api/routes/vectorize.py` (`vectorize_layers`) no tiene ningún default de color — confirmado que NO hay ningún default que fuerce B/N en el contrato; la causa es la ausencia total de un mecanismo de color, no un default incorrecto.
- **Confirmar que colorId/paletteId/layerId sobreviven la serialización** — confirmado con evidencia real: en las respuestas de `detect`/`layers` de todos los fixtures, `groupId`, `paletteId` y `vectorId` son estables y coherentes end-to-end (ver los JSON en `evidence/`); esto ya lo había confirmado el orquestador para `colorHex`/`groupId`/`paletteId`/`vectorId`, lo re-verifiqué con mi propio dataset.
- **Corregir la causa comprobada** — `SvgFillWriter` + wiring en `VectorLayerService`, ver sección 2.
- **Regresión B/N explícita** — corrida manual completa del pipeline M1 (Preprocess → Threshold → Vectorize, sin paleta) contra el backend YA RECOMPILADO con el fix: `evidence/14_bn_pipeline_m1_unchanged_after_fix.svg` sigue devolviendo `fill="#000000"` sin ningún cambio (`VectorizationEndpoints`/`VectorizationService` nunca llaman a `SvgFillWriter`). Además, `dotnet test` completo (605/605) y `pytest` completo (362/362, sin tocar ningún archivo de `services/python-engine`) siguen en verde.
- **Telemetría/logs** — `LogInformation` en `VectorLayerService.GenerateLayersAsync`, verificado en logs reales del contenedor (sección 3).
- **Tests**:
  - Python: no se tocó ningún archivo de `services/python-engine` (el fix es 100% .NET) — no se agregaron tests Python nuevos; se re-corrió el suite completo (362/362) para confirmar que el contrato de `/vectorize-layers` sigue devolviendo exactamente lo que .NET necesita (svg/content_type/width/height/metrics por group_id), sin cambios.
  - .NET: `SvgFillWriterTests.cs` (9 tests unitarios: reemplaza fill negro, no toca `d`, preserva otros atributos, agrega fill si no había, múltiples `<path>`, agujero/subpaths anidados preservando `fill-rule`, XML malformado, raíz no-svg, SVG sin ningún `<path>`) + 5 tests nuevos en `VectorLayerServiceTests.cs` (fixture de 4 colores replicando la reproducción manual — DoD explícito, 2 colores con fill real verificado byte a byte, `d` nunca mutado, telemetría verificada con un `RecordingLogger<T>` propio). `FakePythonVectorLayerClient` se actualizó para devolver `fill="#000000"` hardcodeado (replicando el motor real) — así los tests fallan de verdad si `VectorLayerService` dejara de sobreescribirlo.
  - Frontend: `LayersPanel.test.tsx`, 2 tests nuevos — confirman que cada capa del canvas combinado apunta a una URL de SVG distinta (una por `vectorId`) y que el contenido real detrás de cada `<img src>` (simulado como ya corregido del lado del servidor) tiene un `fill` distinto por capa y nunca `#000000`.
  - Regresión B/N: reproducción manual real contra Docker (sección 3) + suite `.NET`/Python completos sin regresiones.

### 5. Confirmación explícita: el pipeline B/N NO se rompió

- `VtracerEngine`, `VectorizationService`, `VectorizationEndpoints` (M1-S03/S04/S05): **cero líneas modificadas**.
- `SvgFillWriter` solo se invoca desde `VectorLayerService.GenerateLayersAsync` (M2-S02) — ningún otro caller.
- Verificado con una corrida manual real del pipeline M1 completo (Preprocess→Threshold→Vectorize) contra el backend YA recompilado con el fix: el SVG resultante sigue siendo `fill="#000000"` sin cambios (`evidence/14_bn_pipeline_m1_unchanged_after_fix.svg`).
- `dotnet test` completo: 605/605 (592 preexistentes + 13 nuevos), ninguno de los preexistentes tuvo que modificarse para pasar salvo el fixture interno `FakePythonVectorLayerClient` (fixture de TEST, no código de producción) al que se le agregó `fill="#000000"` explícito para que la prueba de regresión sea honesta.
- `pytest` (services/python-engine) completo: 362/362, ningún archivo de producción de Python tocado.

### 6. Verificación — resultados exactos de los 4 suites (completos, no solo nuevos)

- **`dotnet build backend/Vectorify.sln`** → Compilación correcta, 0 advertencias, 0 errores.
- **`dotnet test backend/Vectorify.sln`** → `Correctas! - Con error: 0, Superado: 605, Omitido: 0, Total: 605` (592 preexistentes + 13 nuevos: 9 en `SvgFillWriterTests` + 4 en `VectorLayerServiceTests`).
- **`npm run build`** (frontend, `tsc -b && vite build`) → build y typecheck OK, sin errores.
- **`npm run lint`** (oxlint) → sin hallazgos.
- **`npm test`** (vitest) → `Test Files 23 passed (23)`, `Tests 177 passed (177)` (175 preexistentes + 2 nuevos).
- **`pytest`** (services/python-engine, corrido dentro del contenedor real con `requirements.txt` + `requirements-dev.txt`) → `362 passed, 1 warning in 6.67s` (warning preexistente de `starlette`, no relacionado). Ningún archivo de Python fue modificado en este sprint.
- **Verificación end-to-end real contra Docker**: `docker compose build backend && docker compose up -d backend` con el fix aplicado, seguido de la reproducción manual completa (upload → detect → confirm → layers → GET SVG) para 2, 4, 6 colores, antialiasing y transparencia — todas las capas devuelven su color real (sección 3). Pipeline B/N (Preprocess→Threshold→Vectorize) re-verificado manualmente sin cambios.

### Supuestos y decisiones documentadas

- Elección de aplicar el fix en .NET (no en Python): siguiendo la recomendación del orquestador, por las razones de aislamiento de riesgo ya explicadas en la sección 2.
- Dataset de reproducción: generado con OpenCV propio (no reutilicé el fixture exacto del orquestador) para tener una confirmación genuinamente independiente; los colores BGR usados producen los mismos `colorHex` (`#ff0000`, `#00c800`, `#ffdc00`, `#0000ff`) que reportó el orquestador porque la conversión BGR→hex de `color_palette_service._bgr_to_hex` es determinista sobre los mismos valores de entrada — coincidencia esperada, no búsqueda de un resultado particular.
- El hallazgo de SVGs sin `<path>` en grupos minúsculos de antialiasing (sección 1, "Diferencia encontrada") se documenta pero NO se corrige — no es parte de la causa raíz de este bug (afecta igual antes y después del fix) y está fuera del alcance declarado ("no reescribir el pipeline por intuición... corregir únicamente la causa comprobada").
- No se agregó ninguna dependencia nueva (ni NuGet ni npm ni pip): `SvgFillWriter` usa `System.Xml.Linq`, ya presente (mismo que `SvgDimensionWriter`).
- Los contenedores Docker (`vectify-python-engine-1`, `vectify-backend-1`) usados para la reproducción son los que el usuario ya tenía corriendo; el de `backend` se reconstruyó (`docker compose build backend`) para incluir el fix antes de la evidencia "after". No se tocó `docker-compose.yml` ni ninguna configuración de despliegue.

No se tocó nada de `.sprint/` salvo este `IMPL.md` y la carpeta `evidence/` que crea (ambos son el deliverable pedido explícitamente por esta tarea). No se tocó `.claude/`. No se hizo commit, push, merge ni se tocó Notion.
