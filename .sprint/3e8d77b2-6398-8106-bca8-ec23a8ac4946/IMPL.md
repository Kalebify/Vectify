status: ok

## M2.1-S03 — Color → Layers vectoriales independientes (endurecimiento de M2-S02/M2-S03/M2-S07)

### 1. Dónde vive la validación raster-vs-vector, y por qué

**En Python**, dentro de `POST /api/v1/vectorize-layers` (`app/api/routes/vectorize.py`), no en .NET.
Razones:

- Python ya recibe las N máscaras binarias de la paleta en una única request (M2-S02) y ya las
  decodifica a `numpy.ndarray` para trazarlas (`VectorizationService.process`). La validación
  necesita exactamente esa misma máscara decodificada (no una re-decodificada aparte que podría
  divergir sutilmente) Y necesita la unión de las máscaras de las DEMÁS capas para la detección de
  contaminación cruzada — datos que **ya están todos juntos, en memoria, en esa misma request**.
  Hacerlo en .NET hubiera exigido: (a) añadir un rasterizador de SVG a .NET (dependencia nueva no
  justificada por el diseño), o (b) reenviar streams ya consumidos, complicando el control de flujo.
- Python ya tiene `cv2`/`numpy` (usados en todo el resto del motor); no se agregó ninguna dependencia
  nueva (`cv2.fillPoly`, `cv2.bitwise_xor/and`, `cv2.resize` con `INTER_AREA` — todo ya disponible vía
  `opencv-python-headless`, ya en `requirements.txt`).
- Reutiliza `app.core.svg_path_parsing.collect_document_subpaths` (ya compartido con
  `component_analysis`, M2-S03) en vez de duplicar el tokenizer de paths SVG.

`VectorLayerService.cs` (.NET) **no recalcula nada**: recibe `raster_validation` ya calculado en la
respuesta de Python (`PythonVectorLayerClient` lo parsea/valida defensivamente, mismo criterio que
bounds/métricas), lo persiste como parte de `VectorLayer.RasterValidation`, y solo decide si loggear
una advertencia (`ILogger.LogWarning`) — sin tocar los números.

### 2. Rasterización SVG→máscara: el bug de +1 fila/columna, y cómo se corrigió (con evidencia)

`cv2.fillPoly` rasteriza un polígono incluyendo su borde LEJANO (columna/fila máxima), no solo el
cercano. Confirmado empíricamente: un cuadrado de 30×30 con vértices EXACTOS en la grilla de píxeles
(`M15,15 L45,15 L45,45 L15,45 Z`, la forma real que emite `VtracerEngine` `mode="polygon"`) se
rasterizaba de vuelta como 31×31 (961 píxeles en vez de 900) si se rellenaba directo a la resolución
final — un sesgo sistemático que por sí solo ya producía:

```
cuadrado 60/30 (sin corrección):    own_mismatch = 6.78%
círculo/anillo 80/30/12:            own_mismatch = 7.98%
bloques dispersos 4x4 (M2-S02/S03): own_mismatch = 56.25%  <- inutiliza la métrica en formas chicas
```

**Corrección**: `app/core/raster_validation.rasterize_svg_mask` rasteriza a una resolución
SUPERMUESTREADA (factor `scale`, adaptativo según el tamaño del bounding box de la capa, acotado por
un presupuesto de 4.000.000 de píxeles supermuestreados — nunca todo el lienzo completo, solo el
recorte que contiene geometría) y reduce con `cv2.resize(..., interpolation=cv2.INTER_AREA)` + umbral
al 50% — equivalente a "el píxel entra si su CENTRO cae dentro del polígono". Verificado exacto (0
mismatch) para el cuadrado de 30×30 y el bloque de 4×4 con `scale=8`; el círculo con agujero baja de
7.98% a 2.86% (el resto es aproximación geométrica REAL de VTracer, no un artefacto — ver §3). El
supermuestreo se limita al bounding box de la propia capa (+1px de margen), no al lienzo completo, así
que el costo no escala con el tamaño de la imagen completa, solo con el de cada capa individual.

### 3. Umbrales elegidos, con evidencia empírica (script de reproducción, no incluido en el repo — resultados documentados acá)

**`raster_validation_own_mismatch_tolerance = 0.15` (15%)**. Peor caso observado tras la corrección de
rasterización: formas CURVAS chicas (aproximación poligonal genuina de VTracer, no un bug):

```
círculo r=5px  (lienzo 20x20): 9.88%
círculo r=8px  (lienzo 30x30): 5.58%
círculo r=10px (lienzo 40x40): 5.36%
círculo r=15px (lienzo 60x60): 3.10%
círculo r=25px (lienzo 80x80): 2.40%
círculo r=40px (lienzo 120x120): 1.51%
círculo r=80px (lienzo 200x200): 0.89%
```

Todas las formas de bordes RECTOS (cuadrados, bloques dispersos, cuadrantes) miden 0.00% una vez
corregida la rasterización. 15% deja ~1.5× de margen sobre el peor caso observado (círculo de 5px de
radio) sin dejar de ser sensible a errores reales: una capa vectorizada a partir de la máscara
EQUIVOCADA mide un mismatch de decenas de puntos porcentuales, muy por encima de este umbral.

**`raster_validation_contamination_tolerance = 0.01` (1%)**. Peor caso observado de DOS COLORES
CONTIGUOS (que se tocan en un borde recto compartido, sin solaparse — el caso de "falso positivo" que
spec.md pide evitar explícitamente):

```
lienzos chicos/medianos (60x60 a 500x500, escala de supermuestreo 8x-4x): 0.00% contaminación
lienzo 1000x1000 (presupuesto de supermuestreo cae a escala 1x):         0.20% contaminación
lienzo 2000x2000 (escala 1x):                                            0.10% contaminación
```

1% deja ~5× de margen sobre el peor caso observado, y es deliberadamente MÁS ESTRICTO que
`own_mismatch_tolerance`: la contaminación cruzada es la señal de mayor impacto que esta tarjeta pide
priorizar (geometría de un color invadiendo el territorio de otro), así que el umbral es más
conservador. Verificado también con contaminación REAL simulada (unit test
`test_reconstructed_area_overlapping_another_layers_mask_is_flagged_as_contamination`): una invasión
deliberada del 25-33% del área propia se detecta correctamente muy por encima del umbral.

Ambos valores en `app/core/config.py` (`Settings.raster_validation_own_mismatch_tolerance` /
`raster_validation_contamination_tolerance`), documentados con la misma evidencia en el propio código
(comentarios extensos, mismo estilo que `color_palette_default_tiny_area_ratio` de M2.1-S02).

### 4. Bloquear vs. advertir: se eligió ADVERTIR (nunca bloquear)

Decisión tomada según la recomendación del orquestador en spec.md, sin evidencia que la contradijera:
una discrepancia fuera de tolerancia se loggea (`logger.warning` en Python, `ILogger.LogWarning` en
.NET al recibir el resultado) pero la capa se genera y persiste igual — la request de
`vectorize-layers` sigue siendo "todo o nada" únicamente respecto a errores DUROS de trazado (máscara
corrupta/vacía/timeout, sin cambios de esta tarjeta), no respecto a esta validación de calidad nueva.
Motivo: la validación puede tener falsos positivos legítimos en casos límite no anticipados (formas
extremadamente pequeñas o inusuales) y bloquear la generación completa de un conjunto de capas por una
sola advertencia de una capa sería desproporcionado — el usuario sigue pudiendo revisar/corregir con
las herramientas de M2.1-S06/S07 (fuera de alcance de esta tarjeta).

### 5. Modelo `VectorLayer` consolidado — endpoint nuevo, no modificación del contrato M2-S02

`GET .../color-palette/{paletteId}/layers/consolidated` (`ConsolidatedVectorLayerEndpoints.cs`, nuevo)
combina, sin recalcular nada:

- `Id`/`Name`/`ColorHex`/`Fill`/`VectorId`/`SvgUrl` — de `IVectorLayerService.FindLatest` (M2-S02/M2.1-S01).
- `ComponentCount` (`int?`, null si M2-S03 no se calculó para ese VectorId) — de
  `IComponentAnalysisService.FindLatest`, SIN disparar un cálculo nuevo.
- `ManufacturingOperation` (wire value, "unassigned" por defecto) — de
  `IManufacturingOperationService.FindCurrent`, SIN mutar nada.
- `Visible=true`, `Locked=false`, `Order=`posición en la lista — valores DEFAULT/COMPUTADOS, ver §6.
- `RasterValidation` — ya persistido en `VectorLayer` (ver §1), sin recalcular.

**Decisión: endpoint NUEVO, no una modificación de `VectorLayerEndpoints`/`VectorLayerSetResponse`**
(M2-S02) — ese contrato ya lo consumen M2-S07 (`ManufacturingOperationEndpoints`) y potencialmente
React; cambiarlo hubiera sido un riesgo de romper consumidores existentes sin necesidad. El nuevo
endpoint es puramente aditivo.

**`Fill` vs. `ColorHex`**: spec.md pide ambos (`fill`, `colorId`/`colorHex`) en el modelo mínimo. Hoy
son literalmente el mismo valor (`SvgFillWriter`, M2.1-S01, pinta el SVG con `ColorHex` tal cual) —
se exponen los dos campos igual, documentando que no hay divergencia posible con la implementación
actual, en vez de inventar un campo `Fill` con lógica propia que no existe.

**`ColorId` → se usó `ColorHex`, no un ID paralelo**: el codebase entero (ColorGroup, todos los
payloads existentes) ya usa `ColorHex`; no existe ni hace falta un "id de color" distinto de `GroupId`
(que ya identifica la capa) — introducir uno sería un tipo paralelo sin consumidor.

**`paths[]` → se usó `SvgUrl`**: spec.md acepta explícitamente "paths[] O referencia al SVG" — se
reutilizó el mismo criterio YA establecido por `VectorLayerPayload.SvgUrl` (M2-S02) en vez de
duplicar/re-serializar la geometría del `<path>` a JSON.

### 6. `visible`/`locked`/`order`: sin persistencia interactiva nueva (ver ambigüedad de spec.md)

Tal como recomienda spec.md ("Ambigüedades detectadas"), esta tarjeta expone los 3 campos con valores
DEFAULT/COMPUTADOS razonables:

- `Visible = true` siempre.
- `Locked = false` siempre.
- `Order` = índice (0-based) en `VectorLayerSetVersion.Layers`, la misma lista que ya devuelve M2-S02
  (orden estable mientras no se regenere el conjunto de capas).

**No se adelantó la persistencia real** (mutación de estos 3 campos) — spec.md asigna esa
responsabilidad explícitamente a M2.1-S07 ("Persists order, name, visible, locked, and operation").
Adelantarla acá hubiera significado: (a) diseñar un registro de estado nuevo sin conocer todavía la
forma exacta de la interacción de M2.1-S07 (drag-and-drop, checkboxes, etc. — explícitamente FUERA de
alcance de esta tarjeta), con riesgo de tener que rehacerlo; (b) invertir esfuerzo en un endpoint de
mutación que M2.1-S07 probablemente reemplace de todos modos. Se documenta acá para que M2.1-S07 lo
reutilice como base del contrato de lectura, construyendo la mutación encima.

**Test explícito de que reordenar no altera geometría**
(`GenerateLayersAsync_ReversingTheReportedLayersOrderNeverChangesAnyGroupsVectorIdOrPersistedSvg`,
.NET): invierte la lista de `Layers` reportada (simulando lo que haría reordenar) y confirma que
`VectorId` y el SVG persistido por `GroupId` no cambian — la geometría vive en su propia
`VectorVersion`, completamente independiente de la posición en la lista.

### 7. Cobertura de "Pruebas" de spec.md

| Caso | Dónde |
|---|---|
| 4 colores | `test_vectorize_layers_four_colors_each_report_zero_contamination_against_the_other_three` (Python) + `GenerateLayersAsync_WhenPaletteHasFourColors_...` (.NET, ya existente de M2.1-S01, reverificado) |
| Huecos (topología con agujeros) | `TestRasterizeSvgMask.test_hole_topology_xor_composition_produces_a_ring_not_a_solid_disk`, `test_vectorize_layers_a_ring_with_a_hole_reports_an_own_mismatch_within_tolerance` |
| Componentes separados del mismo color | `test_vectorize_layers_separate_components_of_the_same_color_report_a_raster_validation_within_tolerance` (bloques 4×4 dispersos — el caso límite que motivó la corrección de rasterización de §2) |
| Transparencia | `test_vectorize_layers_mask_from_partial_alpha_group_reports_a_raster_validation_within_tolerance` |
| Colores CONTIGUOS (sin falsos positivos) | `test_vectorize_layers_contiguous_colors_touching_at_a_straight_border_report_no_false_positive_contamination` + `TestCompareLayerRaster.test_touching_but_non_overlapping_masks_report_zero_contamination_no_false_positive` |
| Contaminación cruzada SÍ detectada | `TestCompareLayerRaster.test_reconstructed_area_overlapping_another_layers_mask_is_flagged_as_contamination` |
| Reordenar no altera geometría | `GenerateLayersAsync_ReversingTheReportedLayersOrderNeverChangesAnyGroupsVectorIdOrPersistedSvg` (.NET) + `test_vectorize_layers_reorder_of_group_ids_does_not_change_any_layers_svg_or_metrics` (Python) |
| IDs persisten | `FindLatest_AfterGenerating_ReturnsTheRecord`, `GenerateLayersAsync_EachLayerIsRegisteredAsAReusableVectorVersion` (ya existentes, M2-S02) + `test_vectorize_layers_is_deterministic_across_repeated_requests` (ya existente) |
| Advertir, no bloquear | `GenerateLayersAsync_WhenRasterValidationIsOutOfTolerance_LogsAWarningButStillGeneratesTheLayer` |
| Modelo consolidado | `ConsolidatedVectorLayerEndpointsTests` (3 tests: defaults, combinación real con componentes+operación, 404) |

### 8. Ambigüedades resueltas

- **Umbral de tolerancia**: no cuantificado por spec.md — resuelto con evidencia empírica, ver §3.
- **Bloquear vs. advertir**: no especificado — se siguió la recomendación del orquestador (advertir), ver §4.
- **Dónde vive el cálculo (Python vs .NET)**: spec.md sugería ambas opciones — se eligió Python, ver §1.
- **`visible`/`locked`/`order`**: se siguió la recomendación del orquestador (defaults, sin adelantar persistencia) — ver §6.
- **Endpoint nuevo vs. modificar el contrato existente**: se eligió un endpoint nuevo para no arriesgar romper consumidores de M2-S02/M2-S07 — ver §5.

### 9. Archivos creados

**Python (`services/python-engine/`):**
- `app/core/raster_validation.py` — `rasterize_svg_mask`, `compare_layer_raster`, `RasterValidationOutcome`.
- `tests/test_raster_validation.py` — 10 tests unitarios puros.

**Backend (`backend/Vectorify.Api/`):**
- `VectorLayers/LayerRasterValidation.cs`
- `Contracts/ConsolidatedVectorLayerResponse.cs`
- `Endpoints/ConsolidatedVectorLayerEndpoints.cs`

**Backend tests:**
- `EndToEnd/ConsolidatedVectorLayerEndpointsTests.cs` — 3 tests de integración HTTP.
- `TestSupport/VectorizeLayersPayloads.cs`

### 10. Archivos modificados

**Python:**
- `app/api/routes/vectorize.py` — `vectorize_layers` calcula `raster_validation` por capa (propia + contaminación cruzada), loggea advertencias.
- `app/core/config.py` — `raster_validation_own_mismatch_tolerance`/`raster_validation_contamination_tolerance`.
- `app/models/schemas.py` — `RasterValidationResult`, `VectorLayerItem.raster_validation`.
- `app/services/vectorization_service.py` — `process_with_mask` (refactor mínimo, preserva `process` intacto).
- `tests/support.py` — `make_quadrant_mask_png_bytes`, `make_adjacent_masks_png_bytes`.
- `tests/test_vectorize_layers_route.py` — 8 tests nuevos + 2 fixtures ajustadas a mismo tamaño de lienzo (invariante real de M2-S02 del que depende la validación).

**Backend:**
- `VectorLayers/VectorLayer.cs` — campo `RasterValidation`.
- `VectorLayers/VectorLayerService.cs` — propaga `RasterValidation`, loggea advertencia si corresponde.
- `Clients/PythonVectorLayerResult.cs` — `PythonVectorLayerItemResult.RasterValidation`.
- `Clients/PythonVectorLayerClient.cs` — parsea/valida defensivamente `raster_validation`.
- `Contracts/PythonVectorizeLayersPayload.cs` — `PythonRasterValidationPayload`.
- `Program.cs` — registra `MapConsolidatedVectorLayerEndpoints()`.

**Backend tests:**
- `Clients/PythonVectorLayerClientTests.cs` — 3 tests nuevos + fixture `LayerJson` actualizada.
- `ManufacturingOperations/ManufacturingOperationServiceTests.cs` — constructores de `VectorLayer` actualizados.
- `VectorLayers/PersistentVectorLayerSetRegistryTests.cs` — constructor de `VectorLayer` actualizado.
- `VectorLayers/FakePythonVectorLayerClient.cs` — `DefaultRasterValidation()`.
- `VectorLayers/VectorLayerServiceTests.cs` — 3 tests nuevos (propagación, advertencia, reordenar).
- `TestSupport/FakePythonPreprocessServer.cs` — rutas simuladas `/api/v1/color-palette`, `/api/v1/vectorize-layers` (con eco real de `group_ids`), `/api/v1/components`.

### 11. Verificación — resultados exactos de los 4 suites (completos, no solo nuevos)

- **`dotnet build Vectorify.sln`** → Compilación correcta, 0 advertencias, 0 errores.
- **`dotnet test Vectorify.sln`** → `Correctas! - Con error: 0, Superado: 631, Omitido: 0, Total: 631` (622 preexistentes + 9 nuevos: 3 en `PythonVectorLayerClientTests` + 3 en `VectorLayerServiceTests` + 3 en `ConsolidatedVectorLayerEndpointsTests`).
- **`pytest`** (`services/python-engine`, `.venv` local) → `400 passed, 1 warning in ~8s` (382 preexistentes + 18 nuevos: 10 en `test_raster_validation.py` + 8 en `test_vectorize_layers_route.py`). El warning es preexistente (`starlette`, no relacionado).
- **`npm run build`** (frontend) → **falla por un error PREEXISTENTE, no relacionado con esta tarjeta**: `tsc -b` reporta 4 errores de tipos en `src/components/colorPalette/ColorPalettePanel.test.tsx` (líneas 259/281, conversión de `undefined` a `RequestInit`). Confirmado preexistente: `git status` muestra CERO archivos de `frontend/` tocados en este sprint (no se modificó nada de frontend). No se intentó arreglarlo (fuera del alcance de esta tarjeta de backend).
- **`npm test`** (vitest) → flaky preexistente (mismo patrón ya documentado en el reporte de M2.1-S02): una corrida mostró `8 failed, 172 passed` (fallas en `PhysicalUnion.test.tsx` y otros, ninguno relacionado a esta tarjeta), una segunda corrida inmediata mostró `Test Files 23 passed (23)`, `Tests 180 passed (180)` — confirma que es inestabilidad de entorno/timing preexistente, no una regresión introducida acá (cero archivos de frontend tocados).

### Supuestos y decisiones no triviales

- **Cálculo de la validación en Python, no en .NET** — ver §1.
- **Corrección de rasterización por supermuestreo adaptativo (no una dependencia de rasterización SVG nueva)** — ver §2.
- **Umbrales `own_mismatch_tolerance=0.15` / `contamination_tolerance=0.01`, con evidencia empírica** — ver §3.
- **Advertir, nunca bloquear** — ver §4.
- **Endpoint consolidado NUEVO, no modificación del contrato de M2-S02** — ver §5.
- **`visible`/`locked`/`order` con defaults, sin adelantar la persistencia interactiva de M2.1-S07** — ver §6.
- El build/test de frontend no se tocó ni se intentó arreglar (fuera de alcance; falla preexistente confirmada por `git status`).

No se tocó `.sprint/` más que este archivo, ni `.claude/`. No se hizo commit, push, merge ni se tocó Notion.
