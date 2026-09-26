# M2-S03 · Componentes independientes por capa — Notas de implementación

## 1. Split de stack elegido y por qué

Se siguió la recomendación del orquestador (spec.md, "Ambigüedades detectadas"): el cálculo
geométrico de *connected components* corre en **Python**, reutilizando la infraestructura YA
compartida de M1-S08 (`app.core.svg_path_parsing`: tokenizer de `d`, extracción de subpaths,
resolución de `transform="translate(...)"`), y **ASP.NET Core** aporta una capa fina que:

1. localiza el SVG de origen — cada capa de M2-S02 YA ES una `VectorVersion` normal, así que se
   resuelve por `IVectorizationService.FindVector`, exactamente igual que Check/Dimension;
2. llama a Python (`POST /api/v1/components`, nuevo endpoint, mismo patrón multipart file+params
   que `/check`);
3. persiste el resultado versionado (`ComponentSetVersion`, sidecar JSON en disco, mismo patrón
   cache+lock+versionado que `PersistentVectorLayerSetRegistry`/`PersistentDimensionVersionRegistry`);
4. lo expone a React (`GET/POST .../vectors/{vectorId}/components`).

Justificación: mantiene el patrón arquitectónico único de TODO el proyecto ("Python analiza, .NET
persiste/orquesta/expone, React consume") y evita reimplementar en C# un tokenizer de paths SVG ya
resuelto y testeado. Se descartó calcular esto en el navegador (rompería "el navegador no es la
fuente de verdad") y se descartó reimplementarlo en C# (duplicaría lógica ya correcta y abriría
la puerta a que las dos implementaciones divergieran, el mismo riesgo que motivó extraer
`svg_path_parsing.py` en M1-S08).

**Extensión de infraestructura compartida (sin tocar M1-S08):** se agregaron dos funciones
públicas nuevas a `app/core/svg_path_parsing.py` (`resolve_translate_transform`,
`collect_document_subpaths`), generalizando la recolección de subpaths en coordenadas absolutas
que `app.core.path_checker` ya hacía de forma privada. **No se modificó `path_checker.py`**
(fuera de alcance de esta tarjeta, ya testeado): su copia privada del mismo criterio queda intacta;
la nueva infraestructura compartida vive al lado, lista para que un futuro refactor de M1-S08 la
adopte si se decide.

**Clave de caché:** cada `VectorId` (capa) es inmutable una vez generado (M1-S05/M2-S02), así que
alcanza como única clave — no hace falta ninguna combinación adicional de parámetros como en
`VectorLayerService` (paleta+versión) o `DimensionService` (SVG+dimensiones).

**Sin tolerancias ajustables desde Vectify.Api/React:** a diferencia de `CheckOptions`
(`close_gap_ratio`/`duplicate_point_ratio` configurables), esta tarjeta no tiene una UI de "ajustar
tolerancia" (spec.md, "Usuario podrá": solo ver/seleccionar componentes). El cliente .NET envía
`params: {}` y Python aplica sus propios defaults (única fuente de verdad).

## 2. Criterio exacto de "tocarse"

Dos subpaths se consideran la MISMA pieza física si la **distancia mínima segmento-a-segmento**
entre sus contornos (tratando cada subpath como una polilínea, cerrada o no según su propio flag)
es `<= touch_ratio * diagonal_del_bbox_del_svg_completo`, con `touch_ratio = 0.001` (0.1% de la
diagonal) por default.

- Se comparó **borde a borde**, no solo vértice a vértice (a diferencia de
  `path_checker._detect_duplicates`, que compara índice a índice porque ahí ambas formas tienen la
  MISMA cantidad de puntos por construcción): acá dos piezas pueden tener cantidades de vértices
  distintas, y un vértice de una puede caer sobre el BORDE de la otra lejos de sus propios
  vértices — un criterio solo-vértices lo pasaría por alto.
- `touch_ratio` es más estricto que `Check:DefaultCloseGapRatio` (0.5%) a propósito: "tocarse" acá
  decide si dos piezas se fusionan en una sola (mayor impacto que solo avisar de un posible
  defecto), así que el umbral es más conservador.
- Se agregó un descarte rápido O(1) por bounding boxes (expandidos por la tolerancia) antes del
  cálculo O(segmentos²) real, para que el caso común (formas lejos entre sí) sea barato.
- Ver `app/core/component_analysis.py::_polyline_distance`/`_segment_distance` y los tests
  `test_gap_just_below_touch_tolerance_merges_into_one_component` /
  `test_gap_just_above_touch_tolerance_keeps_two_components` (casos límite documentados,
  mismo estilo que M1-S08).

## 3. Umbral de "componente diminuto"

`tiny_area_ratio = 0.0005` (0.05% del ÁREA del bounding box de TODO el SVG de la capa) por
default. **Decisión: se REPORTA igual, nunca se filtra** — se marca `is_tiny: true` en la
respuesta. Justificación: en el dominio de corte láser, una pieza real (aunque diminuta) sigue
siendo una pieza que hay que cortar; filtrarla en silencio podría ocultar una pieza legítima del
diseño (ej. un detalle chico) y el costo de mostrarla marcada es mucho menor que el de perderla.
El frontend puede usar `isTiny` para des-enfatizarla visualmente si quisiera, pero nunca desaparece
de la lista/contador ("Azul: 3 piezas" cuenta TODAS, incluidas las diminutas).

## 4. El problema de contención con contornos concéntricos (hallazgo durante la implementación)

La primera versión usaba el **centroide aritmético** de cada subpath como "punto representativo"
para el test de contención (point-in-polygon). Esto es geométricamente incorrecto para contornos
**concéntricos** (el caso más común de este dominio: un anillo/arandela y su propio agujero
comparten el mismo centro) — el centroide del contorno EXTERIOR cae, por simetría, tan "adentro"
del agujero como el propio centroide del agujero, invirtiendo/confundiendo la relación de
contención (se detectó escribiendo el primer test de "arandela": ambos subpaths terminaban con
profundidad 1, ninguno quedaba "sólido").

Se corrigió con `_inward_point`: un punto tomado cerca de un borde propio del subpath (punto medio
del primer borde no degenerado, empujado levemente hacia adentro a lo largo de la normal,
verificado con ray casting sobre el PROPIO polígono) — pegado a SU PROPIO contorno, lejos de
cualquier contorno anidado más chico. Documentado en el docstring de la función y cubierto por
`test_ring_with_hole_is_a_single_component_with_a_hole_member`,
`test_solid_disk_resting_inside_a_ring_hole_without_touching_is_a_separate_component` y
`test_triple_nested_squares_alternate_solid_hole_solid_by_depth_parity`.

**Regla de contención aplicada:** profundidad de anidamiento (`depth`, cuántos otros subpaths
contienen el punto representativo) por PARIDAD even-odd (misma regla que el propio renderizado
SVG): profundidad par = sólido (suma al área neta), impar = agujero (resta). Un agujero se UNE
como mismo componente a su padre DIRECTO (el contenedor de área más chica, no cualquier ancestro).
Una pieza sólida anidada dentro de un agujero (profundidad par pero geométricamente "adentro" de
otro contorno) NO se une por contención — solo se une si además TOCA el borde (regla 2).

## 5. IDs estables dentro de una versión

`component-1`, `component-2`, ... asignados en el orden de aparición del primer miembro de cada
grupo (mismo `path_index`/`subpath_index` de aparición en el documento) — determinista para el
mismo SVG + mismos parámetros (que son fijos, ver punto 1), igual criterio que
`path_checker._detect_duplicates.cluster_id`. Cubierto por
`test_same_svg_and_params_produce_the_same_ids_in_the_same_order`.

## 6. Archivos creados/modificados

### Python (`services/python-engine`)
- `app/core/svg_path_parsing.py` — **modificado**: agrega `resolve_translate_transform` y
  `collect_document_subpaths` (infraestructura compartida nueva, no toca lo existente).
- `app/core/component_analysis.py` — **nuevo**: algoritmo completo (contención, contacto, área
  neta, IDs estables).
- `app/core/errors.py` — **modificado**: agrega `ComponentAnalysisTimeoutError`,
  `TooManySubpathsForComponentsError`.
- `app/core/config.py` — **modificado**: agrega `component_*` settings (timeout, límite de
  subpaths, defaults de tolerancia).
- `app/models/schemas.py` — **modificado**: agrega `ComponentAnalysisParams`, `ComponentBounds`,
  `ComponentMember`, `ComponentItem`, `ComponentSummary`, `ComponentAnalysisResponse`.
- `app/services/component_analysis_service.py` — **nuevo**: orquestación (sanitiza, timeout con
  hilo separado, mismo patrón que `PathCheckerService`).
- `app/api/routes/components.py` — **nuevo**: `POST /api/v1/components`.
- `app/api/dependencies.py`, `app/main.py` — **modificados**: wiring del nuevo router/servicio y
  mapeo de errores HTTP.
- `tests/test_component_analysis.py`, `tests/test_component_analysis_service.py`,
  `tests/test_components_route.py` — **nuevos**.

### ASP.NET Core (`backend/Vectify.Api`)
- `Components/` (**nuevo módulo**): `ComponentBounds.cs`, `ComponentMember.cs`,
  `LayerComponent.cs`, `ComponentSetVersion.cs`, `IComponentVersionRegistry.cs`,
  `InMemoryComponentVersionRegistry.cs`, `PersistentComponentVersionRegistry.cs`,
  `ComponentSetResult.cs`, `IComponentAnalysisService.cs`, `ComponentAnalysisService.cs`.
- `Clients/IPythonComponentClient.cs`, `PythonComponentResult.cs`, `PythonComponentClient.cs` —
  **nuevos** (mismo patrón defensivo que `PythonCheckClient`).
- `Contracts/PythonComponentPayload.cs`, `Contracts/ComponentResponse.cs` — **nuevos**.
- `Options/ComponentOptions.cs`, `Options/ComponentRegistryOptions.cs` — **nuevos**.
- `Endpoints/ComponentEndpoints.cs` — **nuevo**:
  `POST/GET .../vectors/{vectorId}/components`.
- `Program.cs`, `appsettings.json` — **modificados**: wiring (DI, HttpClient, opciones) y sección
  `Component`/`ComponentRegistry`.
- `Vectify.Api.Tests/Components/*`, `Vectify.Api.Tests/Clients/PythonComponentClientTests.cs`,
  `Vectify.Api.Tests/TestSupport/ComponentPayloads.cs` — **nuevos**.

### Frontend (`frontend`)
- `src/types/components.ts`, `src/api/componentsApi.ts`, `src/hooks/useLayerComponents.ts` —
  **nuevos**.
- `src/components/layers/ComponentTree.tsx` — **nuevo**: árbol "Layer → Components".
- `src/components/layers/LayerCanvas.tsx` — **modificado**: props opcionales
  (`componentsByGroup`/`selected`/`onSelectComponent`) que agregan un overlay clickeable por
  componente sobre cada capa visible, posicionado con `bounds` en porcentaje (mismo sistema de
  coordenadas que `sourceWidthPx`/`sourceHeightPx`, ya establecido por M2-S02) — sin tocar el
  comportamiento existente cuando esas props no se pasan.
- `src/components/layers/LayersPanel.tsx` — **modificado**: agrega el botón "Calcular
  componentes", el árbol y el panel de métricas de la pieza seleccionada.
- `src/App.css` — **modificado**: estilos nuevos (`.component-tree*`, `.layer-canvas__component*`,
  `.layers-panel__sidebar`).
- `src/components/layers/LayerComponents.test.tsx` — **nuevo** (no se tocó
  `LayersPanel.test.tsx` de M2-S02, que sigue pasando sin cambios).

## 7. Cobertura de criterios de aceptación

| Criterio | Cobertura |
|---|---|
| Connected components sobre geometría SVG, agujero = mismo componente | `component_analysis.py` + `test_component_analysis.py` (agujeros, anidamiento múltiple) |
| IDs estables dentro de versión | `test_same_svg_and_params_produce_the_same_ids_in_the_same_order`, `test_analysis_is_deterministic` |
| Bounds, área, relación de contención por componente | `ComponentItem`/`ComponentMember.role` (Python), `LayerComponent`/`ComponentMember` (C#), payload React |
| Persistencia versionada, no recalcular si ya existe | `ComponentAnalysisService` (cache por VectorId) + `ComponentAnalysisServiceTests` (`CallsPythonOnlyOnce`, `ReturnsCachedWithoutCallingPythonAgain`) |
| Árbol Layer → Components, selección bidireccional, métricas | `ComponentTree.tsx` + `LayerCanvas.tsx` overlay + `LayersPanel.tsx` + `LayerComponents.test.tsx` |
| Tests: islas, agujeros, tocándose, tolerancias, diminutos | `test_component_analysis.py` (19 tests, todos los casos explícitos) |
| No modificar geometría | Ningún módulo nuevo escribe SVG; `ComponentAnalysisService` nunca toca `SvgStorageKey` de la `VectorVersion` de origen |

## 8. Ambigüedades resueltas (no cubiertas ya arriba)

- **Un `<path>` puede tener múltiples subpaths (topología `hierarchical=stacked` de VTracer) o
  cada forma puede ser un `<path>` separado** (spec.md ya lo señala como ambiguo): el algoritmo
  opera siempre a nivel de SUBPATH, ignorando de qué `<path>` viene cada uno — indistinto para el
  resultado.
- **Subpaths con <3 puntos** (líneas de 2 puntos): se reportan como componente propio con área 0
  (no participan como candidatos a contenedor/agujero de otros, ya que no delimitan un área).
