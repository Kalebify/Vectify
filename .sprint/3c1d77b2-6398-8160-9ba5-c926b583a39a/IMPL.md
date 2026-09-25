# M2-S01 · Detección y reducción de paleta de colores — Notas de implementación

## Decisión técnica principal: espacio de color para el clustering

Se eligió **Lab (CIELAB)**, obtenido vía `cv2.cvtColor(..., cv2.COLOR_BGR2LAB)` — ya
disponible porque OpenCV es una dependencia existente del proyecto (usada en
`threshold_pipeline`/`pipeline`), así que no se agrega ninguna dependencia nueva.

**Por qué no RGB/BGR puro**: la distancia euclídea en RGB no se corresponde de forma
estable con la diferencia de color *percibida*. Dos colores pueden estar a la misma
distancia RGB pero uno percibirse "casi igual" y el otro "claramente distinto" según la
zona del espacio de color. Para esta herramienta (agrupar colores de un diseño para
asignarles una operación de láser distinta cada uno, M2-S07) esto importa: sombras
(variaciones de luminosidad de un mismo color lógico) y anti-aliasing (mezclas de color
en los bordes) deben tender a fusionarse con una tolerancia razonable, y RGB puro
distorsiona esa tolerancia según el color base.

**Por qué no HSV**: se evaluó como alternativa más liviana, pero el canal Hue es
inestable/ruidoso cuando la saturación es baja — exactamente el caso de grises/casi-
grises, sombras oscuras y bordes semitransparentes (los mismos casos de prueba
explícitos del spec). Lab no tiene esa degeneración: L (luminosidad) y a/b (croma) se
comportan de forma continua y predecible en todo el espacio.

## Algoritmo de clustering (determinista, sin aleatoriedad)

Implementado en `services/python-engine/app/core/color_palette_pipeline.py`:

1. Los píxeles con `alpha == 0` se excluyen por completo (nunca "color"); los de
   `alpha` parcial participan del clustering por su color RGB compuesto pero quedan
   marcados (`has_partial_alpha`).
2. Se calculan los colores únicos de los píxeles restantes (`np.unique(..., axis=0)`,
   que ya ordena lexicográficamente — sin aleatoriedad) y su frecuencia.
3. **Salvaguarda de rendimiento**: si hay más colores únicos que
   `max_palette_unique_colors` (default 512 — pensado para diseños gráficos/logos, no
   fotografías de tono continuo), se re-cuantiza el color a menos niveles por canal
   (potencias de 2, tope en 128 para no degenerar a un único color) hasta entrar en el
   presupuesto. Sigue siendo determinista: misma entrada + mismo presupuesto → mismo
   resultado.
4. Clustering aglomerativo determinista: se procesan los colores en orden descendente
   de frecuencia (empate resuelto por el orden lexicográfico BGR ascendente que ya trae
   `np.unique`); cada uno se fusiona con el cluster existente más cercano en Lab si la
   distancia es ≤ `tolerance`, o funda uno nuevo si no.
5. Si el resultado excede `max_colors`, se fusionan repetidamente los dos clusters más
   parecidos entre sí (unión determinista vía union-find, con la matriz de distancias
   recalculada por iteración) hasta entrar en el límite.
6. El color representativo final es el promedio BGR ponderado por cantidad de píxeles
   de cada cluster fusionado (no una conversión de vuelta desde Lab, para evitar error
   de redondeo doble).

Sin punto de aleatoriedad en ningún paso → **reproducible por construcción**: mismos
parámetros + misma imagen ⇒ mismo resultado, byte a byte, en cada corrida (cubierto por
tests explícitos de determinismo tanto en Python como en el contrato HTTP).

## Arquitectura general (mismo patrón que MVP1, adaptado)

- **Python** (`app/services/color_palette_service.py`, `app/api/routes/color_palette.py`):
  un único endpoint `POST /api/v1/color-palette` que hace TODO el trabajo costoso
  (decodificar, clusterizar, generar máscaras + preview cuantizado), acotado por un
  timeout interno (hilo separado, mismo criterio que `SimplificationService`/
  `PathCheckerService`).
- **.NET** (`Vectify.Api/ColorPalette/*`): el `ColorPaletteService` orquesta CINCO
  operaciones (`DetectAsync`, `MergeAsync`, `UnmergeAsync`, `RenameAsync`,
  `ConfirmAsync`), todas devolviendo un `ColorPaletteVersion` NUEVO (historial
  inmutable, nunca se muta una versión existente — mismo principio que
  `SimplificationVersion`/`ThresholdConfigRecord`).
- **React** (`frontend/src/components/colorPalette/*`): panel interactivo con
  swatches, selección múltiple para fusionar, deshacer fusión, renombrado inline y
  preview cuantizado en vivo (con cache-busting por `?v={version}`).

### Adaptación deliberada del patrón "cache+lock+versionado"

Los precedentes (`SimplificationService`, `ThresholdService`) cachean el resultado de
una llamada COSTOSA a Python contra una clave de (fuente, parámetros). Acá:

- **`DetectAsync`** replica ese patrón EXACTAMENTE: es la única operación que llama a
  Python. Clave de caché = `(projectId, imageId, paletteId, tolerance, maxColors)`.
  Cache-hit (mismo `paletteId` + mismos parámetros ya vistos) reutiliza los grupos/
  máscaras ya generados pero SIEMPRE avanza `Version` (nunca retrocede) — cubierto por
  `ColorPaletteServiceTests.DetectAsync_WhenCalledAgainWithSamePaletteIdAndParams_ReturnsCachedWithoutCallingPythonAgain`.
  Sin `paletteId` (sesión nueva), SIEMPRE llama a Python y genera un `PaletteId` nuevo
  — no hay nada que cachear contra una sesión que todavía no existe.
- **`MergeAsync`/`UnmergeAsync`/`RenameAsync`/`ConfirmAsync`** son ediciones de
  metadata puras sobre la ÚLTIMA versión de una sesión: no hay ninguna llamada externa
  costosa que memoizar (un merge es una operación aritmética/de composición de imagen
  del lado de .NET, ver `MaskCompositor`, sin volver a llamar a Python — mismo
  criterio de "sin round-trip innecesario" que `SvgDimensionWriter`, M1-S09). Aun así
  preservan el PRINCIPIO de versionado inmutable (cada una crea una fila nueva, nunca
  muta) y se serializan con un lock por sesión (`SemaphoreSlim` por
  `projectId/imageId/paletteId`) para que dos ediciones concurrentes no pisen el avance
  de versión de la otra — documentado extensamente en el docstring de
  `ColorPaletteService`.

### Merge/Unmerge sin volver a llamar a Python

Cada `ColorGroup` recuerda:
- `RawGroupIds`: los índices crudos que Python asignó a los clusters originales que lo
  integran (estables durante toda la sesión).
- `MaskStorageKey`: PNG binario (0/255) ya persistido.
- `MergedFrom`: snapshot COMPLETA (recursiva) de los grupos exactos que se fusionaron
  para crear este grupo (null si es un grupo tal cual lo detectó Python).

Un **merge** combina las máscaras existentes con un OR binario (`MaskCompositor`,
SixLabors.ImageSharp — ya era dependencia del proyecto, sin agregar nada nuevo) y
promedia los colores ponderado por píxeles. Un **unmerge** simplemente restaura los
grupos guardados en `MergedFrom` (sin recalcular nada) — y como `MergedFrom` conserva
su propio `MergedFrom` anidado, deshacer funciona nivel por nivel aunque haya habido
varios merges sucesivos. El preview cuantizado se reconstruye desde cero en cada
detección/merge/unmerge a partir de los grupos VIGENTES de esa versión (nunca acumula
estado de versiones previas).

### Confirmación

`ConfirmAsync` marca `IsConfirmed = true` en una versión nueva. Una vez confirmada, la
sesión rechaza merge/unmerge/rename y re-detección (código `palette_confirmed`, HTTP
409) — más estricto que lo que pide literalmente el spec ("deshacer un merge... mientras
la paleta no esté confirmada"), extendido por consistencia a las demás ediciones: no
tendría sentido permitir renombrar o re-detectar una paleta ya declarada "final" para
M2-S02. Documentado como decisión, no como ambigüedad bloqueante.

## Transparencia

- `alpha == 0` (total): excluido por completo del clustering, nunca aparece como grupo
  de color. Se reporta aparte como `transparentPercent` a nivel de imagen.
- `0 < alpha < 255` (parcial): SÍ participa del clustering por su color compuesto, pero
  el grupo resultante queda marcado `hasPartialAlpha = true`.
- Test explícito (`test_process_distinguishes_fully_transparent_background_from_solid_color_of_the_same_hue`,
  y su equivalente en C#) que arma una imagen con el MISMO color RGB en dos mitades —
  una opaca, una transparente — y verifica que la mitad transparente NO cuenta como
  color de paleta mientras la opaca sí, exactamente el criterio de aceptación del spec.

## Ambigüedades resueltas (ver spec.md, "Ambigüedades detectadas")

- **Espacio de color**: Lab, justificado arriba.
- **"Muchos colores"**: se usan imágenes con 50+ y 1024 colores únicos según el nivel
  de test (pipeline vs. servicio/ruta/HTTP), documentado en los propios comentarios de
  los tests (`_many_colors_gradient_image`, `make_gradient_png_bytes`).
- **Límite de tamaño de imagen**: se reutiliza el límite ya validado de
  preprocesamiento/threshold (`max_image_width/height/pixels` de `Settings`), sin
  agregar uno nuevo.

## Archivos creados

### Python (`services/python-engine/`)
- `app/core/color_palette_pipeline.py` — clustering determinista puro (sin I/O).
- `app/services/color_palette_service.py` — orquestación (decodificar, timeout, armar respuesta).
- `app/api/routes/color_palette.py` — `POST /api/v1/color-palette`.
- `tests/test_color_palette_pipeline.py` (21 tests), `tests/test_color_palette_service.py` (16),
  `tests/test_color_palette_route.py` (16).

### Backend (`backend/Vectify.Api/`)
- `ColorPalette/ColorGroup.cs`, `ColorPaletteVersion.cs`, `ColorPaletteParameters.cs`,
  `ColorPaletteParameterValidationResult.cs`, `IColorPaletteParameterValidator.cs`,
  `ColorPaletteParameterValidator.cs`, `IColorPaletteVersionRegistry.cs`,
  `InMemoryColorPaletteVersionRegistry.cs`, `PersistentColorPaletteVersionRegistry.cs`,
  `IColorPaletteService.cs`, `ColorPaletteService.cs`, `ColorPaletteResult.cs`.
- `Clients/IPythonColorPaletteClient.cs`, `PythonColorPaletteResult.cs`, `PythonColorPaletteClient.cs`.
- `Contracts/PythonColorPalettePayload.cs`, `ColorPaletteDetectRequest.cs`,
  `ColorPaletteMergeRequest.cs`, `ColorPaletteUnmergeRequest.cs`, `ColorPaletteRenameRequest.cs`,
  `ColorPaletteResponse.cs`.
- `Options/ColorPaletteOptions.cs`, `ColorPaletteRegistryOptions.cs`.
- `Imaging/MaskCompositor.cs` — combinar máscaras (merge) y construir el preview cuantizado, con ImageSharp.
- `Endpoints/ColorPaletteEndpoints.cs` — detect/merge/unmerge/rename/confirm/get/preview/mask.

### Backend tests (`backend/Vectify.Api.Tests/`)
- `ColorPalette/ColorPaletteParameterValidatorTests.cs` (9), `PersistentColorPaletteVersionRegistryTests.cs` (7),
  `FakePythonColorPaletteClient.cs`, `ColorPaletteServiceTests.cs` (30).
- `Clients/PythonColorPaletteClientTests.cs` (13).
- `TestSupport/ColorPalettePngs.cs`, `TestSupport/ColorPalettePayloads.cs`.

### Frontend (`frontend/src/`)
- `types/colorPalette.ts`, `api/colorPaletteApi.ts`, `hooks/useColorPalette.ts`.
- `components/colorPalette/ColorPalettePanel.tsx`, `ColorSwatchList.tsx`, `ColorPalettePanel.test.tsx` (8 tests).

## Archivos modificados

- `services/python-engine/app/core/config.py` — settings de paleta de colores.
- `services/python-engine/app/core/errors.py` — `ColorPaletteTimeoutError`.
- `services/python-engine/app/models/schemas.py` — `ColorPaletteParams`/`ColorGroupPayload`/`ColorPaletteResponse`/etc.
- `services/python-engine/app/api/dependencies.py`, `app/main.py` — wiring del router/dependencia/capability/status codes.
- `services/python-engine/app/services/info_service.py` — capability `color-palette`.
- `services/python-engine/tests/support.py` — generadores de imágenes de prueba (gradiente, anti-aliasing, sombra, colores casi iguales, colores sólidos).
- `services/python-engine/tests/test_info.py` — guarda de capability actualizada.
- `backend/Vectify.Api/Program.cs`, `backend/Vectify.Api/appsettings.json` — DI + sección `ColorPalette`/`ColorPaletteRegistry` (no se omitió, a diferencia de lo que pasó en M1-S09).
- `frontend/src/App.tsx`, `frontend/src/App.css` — nueva sección "Paleta de colores" (opera sobre `activeProject` directamente, no depende de preprocesamiento/threshold/vectorización).

## Cobertura de criterios de aceptación

| Criterio | Cómo se cubre |
|---|---|
| Endpoint Python: paleta + máscara + % área por grupo | `POST /api/v1/color-palette`, `ColorPaletteResponse.groups[].{color_hex,mask_base64,area_percent}` |
| Clustering en espacio perceptual, tolerancia + número objetivo configurables | Lab, `ColorPaletteParams.tolerance`/`max_colors`, validado en ambas capas |
| Transparencia explícita, distinguible de fondo sólido | alpha=0 excluido; `transparentPercent` separado; test dedicado |
| `ColorPaletteVersion` versionado inmutable | `ColorPalette/ColorPaletteVersion.cs` + registro persistente, cache-hit avanza versión |
| Endpoints detectar/merge/unmerge/rename/confirmar | `ColorPaletteEndpoints.cs`, cliente HTTP con validación defensiva (`PythonColorPaletteClient`) |
| Frontend: swatches, %área, merge/unmerge, preview en vivo, confirmar | `ColorPalettePanel.tsx` + `ColorSwatchList.tsx` + `useColorPalette.ts` |
| Reproducibilidad | Sin aleatoriedad en el pipeline; tests de determinismo en Python y HTTP |
| Tests Python (sólidos, anti-aliasing, sombras, transparencia, casi-iguales, muchos colores) | `test_color_palette_pipeline.py`/`test_color_palette_service.py`/`test_color_palette_route.py` |
| Tests .NET (versión/cache/lock, validación) | `ColorPaletteServiceTests.cs`, `ColorPaletteParameterValidatorTests.cs`, `PersistentColorPaletteVersionRegistryTests.cs` |
| Tests frontend (merge/unmerge/rename/confirm + preview) | `ColorPalettePanel.test.tsx` |
| No genera capas SVG | Ningún código de esta tarjeta produce SVG; `ColorGroup` solo referencia máscaras PNG (entrada declarada para M2-S02) |

## Supuestos documentados

- Valores por defecto de tolerancia (12.0 sobre un rango [0,100] en distancia Lab) y
  límite superior de `max_colors` (64) son un supuesto razonable, no cuantificado por
  el spec — alineados 1:1 entre `Vectify.Api.Options.ColorPaletteOptions` y
  `services/python-engine/app/core/config.py` (mismo criterio de "defensa en
  profundidad duplicada" que el resto del pipeline).
- El "área %" se calcula sobre el total de píxeles de la imagen (incluyendo los
  transparentes en el denominador), no solo sobre los píxeles "con color" — más
  intuitivo para estimar el tamaño real de cada operación de láser futura.
- Confirmar una paleta ya confirmada es idempotente (crea una nueva versión igual,
  marcada confirmada de nuevo) en vez de un error — no hay ambigüedad de negocio real
  en volver a confirmar lo mismo.
