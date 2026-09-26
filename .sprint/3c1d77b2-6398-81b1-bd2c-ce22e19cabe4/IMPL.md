# M2-S06 · Unión física de piezas — IMPL.md

Implementado sobre `sprint/3c1d77b2-union-fisica`, extendiendo el trabajo de M2-S01..M2-S05 ya mergeado. Esta es la primera tarjeta de MVP2 que modifica geometría real (a diferencia de M2-S05, lógica/no destructiva).

## 1. Decisión técnica central: librería de geometría booleana

**Elegida: Shapely 2.1.2** (bindings Python maduros sobre GEOS), **no Clipper2/pyclipper**.

Justificación:
- El único binding Python de Clipper disponible hoy en PyPI es `pyclipper` (1.4.0), que envuelve **Clipper 1.x**, no Clipper2 (no existe un binding Python maduro y mantenido de Clipper2 en este momento — se verificó con `pip index versions pyclipper`, última versión 1.4.0, sin indicios de un paquete "clipper2" en PyPI con bindings Python estables).
- pyclipper no modela nativamente "polígono con agujeros" (opera sobre listas de paths con un fill-rule global por operación, no objetos `Polygon(exterior, [holes])`), no expone `distance`/`nearest_points` entre geometrías, y no valida geometría de entrada (`is_valid`) — hubiera exigido reimplementar a mano gran parte de lo que Shapely ya da resuelto, testeado y usado en producción por todo el ecosistema geoespacial Python (GEOS es el mismo motor C++ que usa PostGIS/QGIS).
- Shapely da EXACTAMENTE las tres primitivas que esta tarjeta necesita: `unary_union` (booleana con preservación de huecos vía `Polygon(exterior, interiors)` + `.difference(...)`), `distance`/`nearest_points` (para decidir bridging vs. unión directa, y para construir el bridge en sí) y `.is_valid`/`.area` (para rechazar geometría autointersectante/degenerada SIN intentar "arreglarla" en silencio).
- Se instaló y ancló en `requirements.txt` (`shapely==2.1.2`, wheel precompilado, sin requerir compilar GEOS en Windows — mismo criterio de "wheels precompilados" que ya motivó elegir VTracer sobre Potrace en M1-S05).

Ver `services/python-engine/app/core/physical_union.py` (docstring del módulo) para el detalle completo.

## 2. Algoritmo (`app/core/physical_union.py`)

1. `component_count_before` = `analyze_svg_components` (M2-S03, sin modificar) sobre el SVG de entrada tal cual llegó.
2. Por cada componente SELECCIONADO (ya agrupado por M2-S03 — sus propios subpaths sólido/agujero YA se sabe que son la misma pieza): se construye su geometría real (`unary_union` de sus sólidos, menos `unary_union` de sus agujeros) — **por componente primero, cruce después**: esto evita que el agujero de una pieza seleccionada corte por error material de OTRA pieza seleccionada que casualmente se superponga en el mismo lugar.
3. Grafo COMPLETO entre las N piezas seleccionadas (distancia real Shapely) → **Árbol de Expansión Mínima** (Prim) → resuelve la ambigüedad de "selección parcial" de spec.md conectando TODAS las piezas seleccionadas con el mínimo bridging total, usando siempre la pieza más cercana como referencia.
4. Por cada arista del MST: si la distancia real ya está `<= touch_ratio` (el MISMO criterio de tolerancia de M2-S03, reutilizado tal cual pide spec.md), no se agrega geometría extra (ya se van a fundir solas). Si no, se agrega un **bridge**: un rectángulo recto entre los puntos más cercanos de cada pieza (`nearest_points`), extendido levemente hacia adentro de cada una para garantizar solape real — deliberadamente el bridge MÁS simple posible, sin optimizar posición/ancho/ruta (ver "Fuera de alcance" de spec.md, MVP3).
5. `unary_union` de piezas + bridges → se serializa cada anillo (exterior o agujero) como su propio subpath `M...Z` dentro de UN `<path fill-rule="evenodd">` nuevo (evenodd = mismo criterio de paridad par/impar que ya usa `component_analysis` para decidir sólido/agujero, sin depender del sentido de recorrido que Shapely le dé a cada anillo). Los subpaths de las piezas seleccionadas se quitan de sus `<path>` de origen (se preserva cualquier OTRO subpath no seleccionado que compartiera el mismo `<path>`; el `<path>` se borra solo si quedó vacío).
6. **Validación post-operación no negociable**: se corre `analyze_svg_components` de nuevo sobre el SVG YA modificado. Si el conteo no es EXACTAMENTE `component_count_before - len(selecciones) + 1` (las piezas NO seleccionadas intactas + exactamente 1 pieza fusionada), se lanza `PhysicalUnionImpossibleError` — **nunca se devuelve ni se persiste un resultado que "parece" unido pero no lo está**. Esto cubre tanto el sub-conteo (bridge no conectó todo) como el sobre-conteo (se fusionó de más con una pieza no seleccionada).

Geometría de entrada autointersectante/degenerada (`Polygon.is_valid == False`, o `<3` puntos, o área ~0): se rechaza EXPLÍCITAMENTE (`PhysicalUnionInvalidGeometryError`), **sin** aplicar `buffer(0)` ni ningún otro "arreglo" automático — el criterio "nunca fingir unión" se extiende acá a "nunca fingir que la geometría de entrada estaba bien".

## 3. Nota importante sobre la rama "solapadas/tangentes" (no negociable revisada, no es una limitación de seguridad)

`analyze_svg_components` (M2-S03) ya funde CUALQUIER par de subpaths a distancia `<= touch_ratio` en el MISMO componente. Por construcción, esto implica que **dos componentes que el usuario puede seleccionar como distintos SIEMPRE están a más de `touch_ratio` de distancia real entre sí** — si no lo estuvieran, M2-S03 ya los habría reportado como una sola pieza y el usuario nunca podría seleccionarlos por separado.

Consecuencia: la rama "ya se tocan/se superponen → unión booleana directa sin bridge" de spec.md es, en un flujo end-to-end real (seleccionar 2+ componentes YA calculados por M2-S03 con la MISMA tolerancia), **matemáticamente inalcanzable** — cualquier unión física de 2+ componentes de M2-S03 necesitará al menos un bridge por construcción del propio M2-S03. Esto **no es una limitación de la validación "nunca fingir unión"** (esa validación es robusta y correcta en el 100% de los casos, ver sección 2 punto 6) — es simplemente que, dado que spec.md pide explícitamente reutilizar el MISMO criterio de tolerancia ya establecido en M2-S03 (ver "Ambigüedades detectadas"), la rama "sin bridge" queda como una salvaguarda de corrección (correcta, barata, se mantiene en el código) más que como un camino realmente transitado por la UI.

Se decidió **mantener la implementación de ambas ramas de todas formas** (en vez de eliminar la rama "sin bridge" para "simplificar"): (a) es lo que pide el spec literalmente, (b) es más robusto ante un futuro cambio de tolerancias, (c) forzar una tolerancia distinta solo para que la demo "luzca" con las dos ramas alcanzables violaría el criterio explícito de reutilizar la MISMA tolerancia. La rama "sin bridge" se testea directamente sobre los helpers privados (`_minimum_spanning_tree`, `_build_bridge`) en `tests/test_physical_union.py`, documentado ahí mismo.

La Definition of Done de spec.md ("cuando es geométricamente posible, el resultado es un componente válido") se cumple igual en el 100% de los casos reales, vía bridging.

## 4. Selección parcial (3+ piezas)

Resuelto con el criterio recomendado por spec.md: se arma un grafo COMPLETO de distancias entre las N piezas seleccionadas y se conecta todo con un Árbol de Expansión Mínima (Prim, O(n²) — n es la cantidad de piezas seleccionadas manualmente por el usuario en una sola operación, siempre chica, sin necesidad de salvaguarda de rendimiento adicional). Cada arista del árbol usa la pieza más cercana como referencia para su propio bridge (o ninguno, si ya están dentro de tolerancia). Si alguna pieza queda geométricamente imposible de conectar de forma segura (geometría inválida en el camino), la operación completa falla con un mensaje explicando cuál componente causó el problema.

## 5. Umbral "tangente" vs "separado"

Reutilizado tal cual: `touch_ratio` (mismo default 0.001 = 0.1% de la diagonal del SVG) que ya usa M2-S03, pasado en la request a `/api/v1/components/union` — hoy no ajustable desde la UI de React (mismo criterio que M2-S03: "Python aplica sus propios defaults", sin control de usuario para esta tolerancia).

`bridge_width_ratio` (nuevo, no existía en M2-S03): ancho del rectángulo de bridge, 2% de la diagonal del SVG por default (`Settings.physical_union_default_bridge_width_ratio`) — supuesto documentado, no cuantificado por spec.md: lo bastante ancho para ser una pieza físicamente cortable con láser, lo bastante angosto para no invadir piezas cercanas no seleccionadas en el caso común.

## 6. Backend .NET — diseño del comando versionado

- **Reutiliza `Vectify.Api.Vectorization.VectorVersion` tal cual** (no un tipo paralelo): al confirmar, se crea una `VectorVersion` nueva (nuevo `VectorId`, nueva entrada en el historial YA existente y ya nunca-destructivo de `IVectorVersionRegistry`) con el SVG fusionado. `SourceMaskId`/`Parameters` se heredan de la `VectorVersion` de origen (no hay máscara/parámetros propios de una unión — es una transformación de un SVG ya vectorizado, no una nueva vectorización desde una máscara).
- **Registro de auditoría propio** (`Vectify.Api.PhysicalUnion.PhysicalUnionVersion`, `IPhysicalUnionVersionRegistry`/`PersistentPhysicalUnionVersionRegistry`): versionado por el `VectorId` de ORIGEN (mismo patrón exacto que `ComponentGroupSetVersion`/`IComponentGroupVersionRegistry` de M2-S05) — guarda qué componentIds se fusionaron, con qué estrategia, y a qué `VectorId` resultante. Esto es SOLO bookkeeping/auditoría; el historial real e inmutable que garantiza "la versión anterior nunca se destruye" es el propio `IVectorVersionRegistry`, que ya cumplía esa propiedad desde M1-S05 (append-only, nunca se sobreescribe una versión existente).
- **Preview y confirm comparten el mismo cómputo** (`PhysicalUnionService.ComputeAsync`, privado): validar selección → localizar `ComponentSetVersion`/`VectorVersion` de origen → llamar a Python → devolver éxito/fracaso. Confirm es la ÚNICA rama que, ante éxito, persiste (nueva `VectorVersion` + registro de auditoría); preview SIEMPRE descarta el resultado sin importar qué pasó — así "cancelar" del lado de React no necesita ningún endpoint propio, ni compensar nada: nunca hubo nada que deshacer.
- **Validación de geometría SVG válida antes de persistir**: `PythonPhysicalUnionClient` aplica defensa en profundidad adicional (mismo criterio que `PythonVectorizeClient`/`PythonComponentClient`) — width/height positivos, bounds finitos y coherentes, estrategia reconocida, y **crucialmente**: revalida que `component_count_after == expected_component_count_after` de la propia respuesta de Python antes de aceptarla como éxito (nunca confía ciegamente en que Python respondió 200 → si alguna vez Python tuviera un bug y respondiera "éxito" con una validación post-operación que no cuadra, Vectify.Api la rechaza igual, tratándola como `InvalidResponse`).
- **Ambigüedad resuelta — la "capa" (M2-S02/VectorLayerSetVersion) NO se actualiza automáticamente**: siguiendo el mismo patrón arquitectónico ya establecido por Simplification/Check/Dimension (que operan sobre un `VectorId` sin que `VectorLayerSetVersion` lo sepa, ver comentarios de `VectorLayerService.cs`), la unión física opera PURAMENTE a nivel de `VectorId` — no reescribe el `VectorId` que una capa de M2-S02 tiene asignado en su `VectorLayerSetVersion`. El frontend resuelve esto con un override LOCAL (`vectorIdOverrides` en `LayersPanel.tsx`): tras confirmar, esa capa pasa a mostrar/operar sobre el `VectorId` nuevo (recalculando sus componentes automáticamente), sin tocar el conjunto de capas persistido del lado del backend. Se documenta como decisión de diseño deliberada, no como un descuido: hacerlo del otro lado (mutar `VectorLayerSetVersion`) habría sido un cambio transversal no pedido por la sección "ASP.NET Core" de la tarjeta.

## 7. Contratos nuevos

- Python: `POST /api/v1/components/union` (`app/api/routes/physical_union.py`) — multipart `file` (SVG) + `params` (JSON de `PhysicalUnionParams`: `selections` con `component_id`/`members`, `touch_ratio`, `tiny_area_ratio`, `bridge_width_ratio`).
- .NET:
  - `POST .../vectors/{vectorId}/physical-union/preview` — 200, nunca persiste.
  - `POST .../vectors/{vectorId}/physical-union/confirm` — 201 si éxito (persiste), 422 si geométricamente imposible (no persiste nada).
  - `GET .../vectors/{vectorId}/physical-union` — último registro de auditoría confirmado a partir de ese VectorId (404 si nunca se confirmó uno).
  - Deliberadamente bajo una URL DISTINTA de `.../components/groups` (M2-S05): "Agrupar" y "Unir físicamente" nunca comparten ni siquiera el path, para que la distinción sea imposible de pasar por alto en el código o en la red.

## 8. Frontend

- `usePhysicalUnion` (hook nuevo, independiente de `useComponentGroups`): reutiliza la selección múltiple YA existente de `useComponentGroups`/`ComponentTree` (confinada a una capa, 2+ piezas) para DISPARAR la unión física, pero mantiene su propio estado de fase/preview/error — preview y "Agrupar" son acciones independientes disparables desde la MISMA selección.
- `ComponentTree.tsx`: con 2+ piezas seleccionadas aparecen AMBOS botones lado a lado — "Agrupar N piezas seleccionadas" (existente, M2-S05) y "Unir físicamente N piezas seleccionadas" (nuevo, clase CSS `component-tree__union-action`, acento de advertencia ámbar/naranja vs. el acento neutro de Agrupar). `PhysicalUnionPanel.tsx` (nuevo) muestra el preview real (imagen embebida vía data-URI del SVG YA calculado, nunca una aproximación), conteo antes/después, estrategia en texto humano, y confirmar/cancelar explícitos; si la unión no fue posible, muestra el motivo con `role="alert"` y NO ofrece confirmar.
- `LayersPanel.tsx`: `vectorIdOverrides` (mapa local `layerGroupId -> VectorId nuevo`, `useMemo`d junto con `layerSet.layers` para no romper la estabilidad referencial de la que dependen `useComponentGroups`/`useLayerComponents` — ver nota de rendimiento abajo) + un `useEffect` que dispara `computeComponents()` automáticamente tras una confirmación exitosa (mismo botón/flujo que "Recalcular componentes", disparado solo, no manual).
- **Nota de rendimiento descubierta durante el desarrollo** (no un defecto del M2-S06 en sí, pero surgió al integrarlo): construir el array de capas "efectivas" con `.map()` en cada render sin memoizar rompía la estabilidad referencial de la que depende el efecto de refetch de grupos de `useComponentGroups` (M2-S05), causando reintentos de fetch innecesarios en cada re-render no relacionado (ej. alternar la vista explotada). Se corrigió con `useMemo` sobre `[layerSet, vectorIdOverrides]`. Documentado acá porque el test de regresión que lo detectó (`ExplodedView.test.tsx`) es de M2-S04/M2-S05, no de esta tarjeta — confirma que no rompimos nada de las tarjetas anteriores.
- **No renombrado**: el checkbox de selección de piezas sigue diciendo "para agrupar" (`aria-label`) aunque ahora dispara DOS acciones posibles — no se le cambió el texto para no romper los tests ya existentes de M2-S05 (`ComponentGroups.test.tsx`); la distinción "Agrupar" vs. "Unir físicamente" queda igualmente clara por los DOS botones con texto/color distintos que aparecen debajo.

## 9. Ambigüedades resueltas (resumen)

| Ambigüedad de spec.md | Resolución |
|---|---|
| Librería de geometría booleana | Shapely 2.1.2 (no Clipper2/pyclipper — ver sección 1) |
| Selección parcial / piezas no relacionadas | Árbol de expansión mínima conecta TODAS las seleccionadas; falla explícita si alguna no se puede conectar de forma segura |
| Umbral tangente/separado | Reutilizado tal cual de M2-S03 (`touch_ratio`, mismo default 0.001) |
| ¿La "capa" (VectorLayerSetVersion) se actualiza sola? | No — mismo patrón que Simplification/Check/Dimension; el frontend resuelve con un override local |

## 10. Archivos creados/modificados

### Python (`services/python-engine/`)
- `app/core/physical_union.py` (nuevo) — algoritmo completo.
- `app/core/errors.py` (mod.) — `PhysicalUnionTimeoutError`, `PhysicalUnionInvalidGeometryError`, `PhysicalUnionImpossibleError`.
- `app/core/config.py` (mod.) — `physical_union_timeout_seconds`, `physical_union_default_bridge_width_ratio` (+min/max).
- `app/models/schemas.py` (mod.) — `PhysicalUnionMemberRef`, `PhysicalUnionSelection`, `PhysicalUnionParams`, `PhysicalUnionResponse`.
- `app/services/physical_union_service.py` (nuevo).
- `app/api/routes/physical_union.py` (nuevo).
- `app/api/dependencies.py`, `app/main.py` (mod.) — wiring del nuevo router/servicio/mapeo de errores.
- `requirements.txt` (mod.) — `+shapely==2.1.2`.
- `tests/test_physical_union.py`, `tests/test_physical_union_service.py`, `tests/test_physical_union_route.py` (nuevos).

### .NET (`backend/Vectify.Api/`)
- `PhysicalUnion/PhysicalUnionVersion.cs`, `IPhysicalUnionVersionRegistry.cs`, `PersistentPhysicalUnionVersionRegistry.cs`, `InMemoryPhysicalUnionVersionRegistry.cs`, `PhysicalUnionOutcome.cs`, `PhysicalUnionPreviewResult.cs`, `PhysicalUnionConfirmResult.cs`, `IPhysicalUnionService.cs`, `PhysicalUnionService.cs` (todos nuevos).
- `Clients/IPythonPhysicalUnionClient.cs`, `PythonPhysicalUnionResult.cs`, `PythonPhysicalUnionClient.cs` (nuevos).
- `Contracts/PythonPhysicalUnionPayload.cs`, `PhysicalUnionRequest.cs`, `PhysicalUnionResponse.cs` (nuevos).
- `Options/PhysicalUnionOptions.cs`, `PhysicalUnionRegistryOptions.cs` (nuevos).
- `Endpoints/PhysicalUnionEndpoints.cs` (nuevo).
- `Program.cs`, `appsettings.json` (mod.) — wiring.
- `Vectify.Api.Tests/PhysicalUnion/*` (nuevos: fakes + `PhysicalUnionServiceTests.cs` + `PersistentPhysicalUnionVersionRegistryTests.cs`).
- `Vectify.Api.Tests/Clients/PythonPhysicalUnionClientTests.cs` (nuevo).

### Frontend (`frontend/src/`)
- `types/physicalUnion.ts`, `api/physicalUnionApi.ts`, `hooks/usePhysicalUnion.ts` (nuevos).
- `components/layers/PhysicalUnionPanel.tsx` (nuevo).
- `components/layers/ComponentTree.tsx` (mod.) — botón/panel de unión física.
- `components/layers/LayersPanel.tsx` (mod.) — wiring, override de VectorId, recompute automático.
- `App.css` (mod.) — estilos `.component-tree__union-action`/`.physical-union-panel*`.
- `components/layers/PhysicalUnion.test.tsx` (nuevo).

## 11. Cobertura de criterios de aceptación

- [x] Selección 2+ componentes + acción "Unión física" distinta de "Agrupar" — botones separados, texto/color distinto, endpoints bajo un path distinto.
- [x] Estrategia según relación geométrica — booleana directa vs. bridge, decidido por `touch_ratio` (ver nota sección 3 sobre alcanzabilidad real de la rama sin bridge).
- [x] Nunca fingir unión — validación post-operación con el MISMO analizador de M2-S03, en Python (autoritativa) Y revalidada de nuevo en el cliente .NET (defensa en profundidad).
- [x] Preview antes/después con conteo — geometría real, nunca aproximada.
- [x] Confirmar/cancelar explícitos, sin persistencia hasta confirmar.
- [x] Backend: comando versionado, nueva VectorVersion, versión anterior intacta, validación de geometría SVG válida antes de persistir.
- [x] UI explica por qué si no es posible, versión previa intacta.
- [x] Frontend distingue Agrupar de Unir físicamente en toda la UI.
- [x] Tests: solapadas/tangentes (helpers privados, ver sección 3), separadas (bridge, end-to-end), con agujeros, geometría inválida, selección parcial (3+ piezas, MST).

## 12. Supuestos documentados

- `bridge_width_ratio` default 2% de la diagonal (no cuantificado por spec.md).
- `PhysicalUnionOptions.TimeoutSeconds` = 30s (.NET), `physical_union_timeout_seconds` = 20s (Python) — más altos que Component:TimeoutSeconds/component_timeout_seconds porque la unión hace booleanas/bridging MÁS una segunda pasada completa del analizador de componentes.
- El "orden" de piezas seleccionadas para el MST es el orden en que el usuario las tildó (irrelevante para la corrección del resultado, solo afecta qué nodo es la "raíz" del árbol — determinista para la misma selección).
