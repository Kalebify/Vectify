# M2-S02 · Color → capas de fabricación — Notas de implementación

## Resumen

Cada `ColorGroup` de una `ColorPaletteVersion` **confirmada** (M2-S01) se convierte en una
`VectorLayer` independiente y alineada: una capa vectorial por color, aislable/ocultable en un
panel "Layers" nuevo, reutilizando el motor de vectorización de M1-S05 sin duplicar lógica de
trazado.

## Ambigüedades resueltas

### 1. "N llamadas server-side vs. endpoint dedicado"

La spec sugería (sin obligar) invocar la función de vectorización de M1-S05 N veces **server-side
dentro de una única request .NET→Python**, en vez de N requests HTTP separadas desde el frontend.
Implementado literalmente así:

- **Python**: nuevo endpoint `POST /api/v1/vectorize-layers` (`app/api/routes/vectorize.py`) que
  recibe N máscaras (`files`, multipart) + sus `group_ids` (JSON array de strings, mismo orden y
  cantidad) en una sola request, y por cada una llama a `VectorizationService.process()` — la
  MISMA clase de M1-S05, sin tocarla ni duplicar `vector_engine.py`/`svg_processing.py` — dentro
  de un loop. Ninguna máscara se recorta a su bounding box antes de vectorizarla (ver punto 3).
  Si cualquier máscara falla, toda la request falla (todo-o-nada): el conjunto de capas nunca
  queda parcialmente generado.
- **.NET**: `VectorLayerService.GenerateLayersAsync` abre las N máscaras ya persistidas
  (`ColorGroup.MaskStorageKey`, de M2-S01) y hace **una sola llamada HTTP**
  (`IPythonVectorLayerClient.VectorizeLayersAsync`) que envía las N máscaras juntas. Cada capa
  resultante se persiste y se registra como una `VectorVersion` normal (ver punto 2).

### 2. Tolerancia de alineación pixel→vector

Elegida: **1 px absoluto** sobre el bounding box del path resultante
(`ALIGNMENT_TOLERANCE_PX` en `tests/test_vectorize_layers_route.py`). Justificación: VTracer con
`mode="polygon"` traza el contorno exacto de los píxeles blancos de la máscara sin
antialiasing/suavizado, así que el error esperado en el caso normal es 0 — 1px ya es una
tolerancia holgada, no ajustada al límite. Se usa un valor **absoluto** (no relativo a la
diagonal, como `CheckParams` de M1-S08) porque acá se compara contra una coordenada de píxel
conocida de antemano por el test (no contra otro subpath arbitrario).

### 3. Normalización de coordenadas — cómo se logró sin lógica nueva

Cada máscara de `ColorGroup` (M2-S01) YA tiene las mismas dimensiones que la imagen original
completa (`ColorPaletteVersion.SourceWidthPx/SourceHeightPx`) — no es un recorte del área del
color. `VectorizationService.process()` (M1-S05, sin modificar) vectoriza la máscara TAL CUAL se
la pasan, sin recortarla a su propio contenido. Conclusión: con solo **no cropear** las máscaras
antes de enviarlas, todas las capas resultantes comparten automáticamente el mismo
`width`/`height`/sistema de coordenadas que la imagen original y encajan exactamente superpuestas
— verificado explícitamente en
`test_vectorize_layers_share_the_same_uncropped_canvas_dimensions_across_layers`. No hizo falta
ningún cálculo de traslación/escala adicional.

## Decisión de diseño no trivial: reutilizar `VectorVersion` literalmente

En vez de crear un modelo paralelo con su propio storage de SVG, `VectorLayer` (nuevo,
`Vectify.Api.VectorLayers.VectorLayer`) solo guarda metadata de color/grupo (`GroupId`, `Name`,
`ColorHex`, `AreaPercent`, `HasPartialAlpha`) + un `VectorId` que apunta a una fila real en el
**mismo** `IVectorVersionRegistry` que ya usa M1-S05. Consecuencias:

- El SVG de cada capa se sirve con el endpoint YA EXISTENTE `GET
  .../vectors/{vectorId}` — no hay endpoint nuevo para servir bytes de SVG.
- El resto del pipeline (Simplification/Check/Dimension/Export, M1-S07..M1-S10) puede operar
  sobre una capa individual pasándole su `VectorId`, exactamente igual que sobre cualquier otro
  vector — sin saber que "vino de una capa de color". Esto no era un requisito explícito de
  M2-S02, pero es una consecuencia gratuita de la reutilización literal del tipo, mencionada
  porque condiciona el diseño de tarjetas futuras (M2-S05..M2-S07).
- `VectorVersion.SourceMaskId` (pensado originalmente para un `ThresholdConfigRecord.MaskId`) se
  reutiliza para guardar el `GroupId` del `ColorGroup` de origen — mismo tipo (`Guid`), semántica
  análoga ("de qué máscara de origen salió este SVG"), documentado acá como supuesto en vez de
  tocar el tipo compartido (evita blast radius sobre M1-S05..M1-S09).
- `VectorLayerSetVersion` (el conjunto completo, versionado por sesión de paleta) SÍ es un tipo
  nuevo — no existía nada análogo a "conjunto de N vectores" en el pipeline anterior.

## Precondición

`VectorLayerService.GenerateLayersAsync`: `ColorPaletteVersion` inexistente → `404 not_found`;
existente pero `IsConfirmed=false` → `409 palette_not_confirmed`. Ambos casos devuelven **antes**
de tocar `IPythonVectorLayerClient` (cubierto por
`VectorLayerServiceTests.GenerateLayersAsync_When...ReturnsXWithoutCallingPython`, que assertan
`pythonClient.CallCount == 0`).

## Cache + lock + versionado

Mismo patrón que `ColorPaletteService`/`VectorizationService`: clave de caché =
`(ProjectId, ImageId, PaletteId, ColorPaletteVersion.Version)` — no hay otros parámetros
ajustables en este sprint. Cache-hit reutiliza el mismo `LayerSetId` y los mismos `VectorId` ya
generados (no vuelve a llamar a Python ni a regenerar SVGs) pero SIEMPRE crea una fila
`Version+1` nueva — nunca retrocede ni "re-sirve" una versión vieja como si fuera la misma. Lock
por `SemaphoreSlim` keyed por `(ProjectId, ImageId, PaletteId)` para que requests concurrentes con
la misma paleta no dupliquen la llamada a Python.

## Fuera de alcance (respetado)

No se agrupan/unen piezas, no se asigna corte/grabado, no se distinguen componentes separados
dentro de una capa — `VectorLayer` es 1:1 con `ColorGroup`, el mismo `<path>` (con sus subpaths
internos de agujeros) que ya devuelve VTracer.

## Archivos creados

### Python (`services/python-engine`)
- `app/api/routes/vectorize.py` — agregado `POST /api/v1/vectorize-layers`.
- `app/models/schemas.py` — agregados `VectorLayerItem`, `VectorizeLayersResponse`.
- `app/services/info_service.py` — capability `vectorize-layers`.
- `tests/support.py` — agregados `make_positioned_square_mask_png_bytes`, `make_sparse_mask_png_bytes`.
- `tests/test_vectorize_layers_route.py` — nuevo, 14 tests.
- `tests/test_info.py` — actualizado.

### .NET (`backend/Vectify.Api`)
- `VectorLayers/VectorLayer.cs`, `VectorLayerSetVersion.cs`, `VectorLayerSetResult.cs`,
  `IVectorLayerSetRegistry.cs`, `InMemoryVectorLayerSetRegistry.cs`,
  `PersistentVectorLayerSetRegistry.cs`, `IVectorLayerService.cs`, `VectorLayerService.cs` — nuevos.
- `Clients/IPythonVectorLayerClient.cs`, `PythonVectorLayerClient.cs`, `PythonVectorLayerResult.cs` — nuevos.
- `Contracts/PythonVectorizeLayersPayload.cs`, `VectorLayerResponse.cs` — nuevos.
- `Endpoints/VectorLayerEndpoints.cs` — nuevo (`POST`/`GET .../color-palette/{paletteId}/layers`).
- `Options/VectorLayerOptions.cs`, `VectorLayerRegistryOptions.cs` — nuevos.
- `Program.cs`, `appsettings.json` — wiring.

### .NET tests (`backend/Vectify.Api.Tests`)
- `VectorLayers/VectorLayerServiceTests.cs` — 15 tests (precondición, cache/lock/versión, reuso
  de `VectorVersion`, errores upstream).
- `VectorLayers/PersistentVectorLayerSetRegistryTests.cs` — 6 tests.
- `VectorLayers/FakePythonVectorLayerClient.cs` — fake de test.
- `Clients/PythonVectorLayerClientTests.cs` — 11 tests (validación defensiva del cliente HTTP).

### Frontend (`frontend/src`)
- `types/vectorLayers.ts`, `api/vectorLayersApi.ts` — nuevos.
- `hooks/useVectorLayers.ts` — nuevo.
- `components/layers/LayerList.tsx`, `LayerCanvas.tsx`, `LayersPanel.tsx` — nuevos.
- `components/layers/LayersPanel.test.tsx` — nuevo, 6 tests (toggle de visibilidad, renderizado
  combinado, estado vacío, error controlado).
- `components/colorPalette/ColorPalettePanel.tsx` — agregado prop opcional `onConfirmed` (no
  rompe la API existente; `ColorPalettePanel.test.tsx` sigue pasando sin cambios).
- `App.tsx`, `App.css` — wiring de la nueva sección "Capas por color".

## Cobertura de criterios de aceptación

| Criterio | Cubierto por |
|---|---|
| Precondición paleta confirmada | `VectorLayerServiceTests` (NotFound/Conflict sin llamar a Python) |
| Vectorización independiente reutilizando motor de M1-S05 | `vectorize.py` llama `VectorizationService.process()`, no reimplementa trazado |
| Normalización de coordenadas | `test_vectorize_layers_share_the_same_uncropped_canvas_dimensions_across_layers` |
| Modelo `VectorLayer` ligado a paleta+`VectorVersion` | `VectorLayer.cs`, `VectorLayerServiceTests.GenerateLayersAsync_EachLayerIsRegisteredAsAReusableVectorVersion` |
| Endpoint que genera todo el set de una vez | `POST .../color-palette/{paletteId}/layers` (una sola llamada Python) |
| Cache+lock+versionado inmutable | `VectorLayerServiceTests` (ciclo cache, concurrencia) + `PersistentVectorLayerSetRegistryTests` |
| Panel Layers (color/nombre/visibilidad + canvas combinado) | `LayersPanel.tsx`, `LayerList.tsx`, `LayerCanvas.tsx` |
| Tests Python (solapadas/no solapadas/agujeros/transparencia/alineación) | `test_vectorize_layers_route.py` |
| Tests .NET (ciclo versión/cache/lock, precondición) | `VectorLayerServiceTests.cs` |
| Tests frontend (toggle visibilidad, render combinado) | `LayersPanel.test.tsx` |
| Fuera de alcance respetado | Ningún código de agrupar/unir/asignar operación/detectar componentes |

## Resultados de verificación (ejecutados por el implementador)

- `pytest` (`services/python-engine`): **282 passed**.
- `dotnet build` (`backend/Vectify.sln`): sin errores ni warnings nuevos.
- `dotnet test` (`backend/Vectify.Api.Tests`): **490 passed, 0 failed**.
- `npm run build` (`frontend`, incluye `tsc -b`): sin errores.
- `npm run lint` (`frontend`, oxlint): sin errores ni warnings.
- `npm test` (`frontend`, vitest): **135 passed** (17 archivos).
