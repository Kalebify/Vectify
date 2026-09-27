status: ok

## M2.1-S02 — Detección y reducción real de paleta (endurecimiento de M2-S01)

### 1. Algoritmo: se mantuvo el clustering Lab ya existente (decisión explícita)

spec.md permite explícitamente mantener "la opción ya existente" en vez de migrar a
k-means/median-cut. Se revisó `app.core.color_palette_pipeline.py` (clustering
aglomerativo determinista en espacio Lab, `_cluster_unique_colors` +
`_merge_down_to_max_colors`) y **no se encontró ninguna razón concreta para
reemplazarlo**: el problema documentado por la auditoría M2.1-S01 (17 grupos en vez
de ~5 lógicos con antialiasing) no es una limitación del algoritmo de clustering en
sí, sino la AUSENCIA de un paso de limpieza posterior que reconozca "este grupo es
demasiado chico para ser un color lógico, sin importar cuán lejos esté en Lab de su
vecino más cercano". k-means no resuelve eso mejor (sigue necesitando un post-proceso
de área mínima) y hubiera significado introducir no-determinismo (semillas
aleatorias) donde hoy no lo hay — se descartó. En cambio, se agregó un paso NUEVO
(`_merge_tiny_groups_into_nearest`) que ataca la causa raíz real: fusiona por ÁREA,
no por distancia de color, ANTES del paso existente que fusiona por `max_colors`.

### 2. Flag incluido/excluido por color (`IsExcluded`)

- `ColorGroup.cs` (.NET) ganó `IsExcluded: bool`, campo independiente de `MergedFrom`
  — un grupo puede excluirse sin fusionarlo a otro.
- Nuevo endpoint `POST .../color-palette/{paletteId}/exclude` (`ColorPaletteSetExclusionRequest{ GroupId, IsExcluded }`,
  `ColorPaletteService.SetExclusionAsync`): mismo patrón exacto que `RenameAsync`
  (edición de metadata pura, versionada, con lock por sesión, nunca cambia el
  `GroupId` de ningún grupo, ni el propio ni los demás).
- **Merge y `IsExcluded`** (ambigüedad no cubierta por spec.md, resuelta y
  documentada acá): un grupo fusionado hereda `IsExcluded=true` solo si **todos**
  los grupos que lo integran ya estaban excluidos (`selected.All(g => g.IsExcluded)`).
  Fusionar un fondo excluido con un color en primer plano no debería "esconder" el
  resultado combinado sin que el usuario lo note explícitamente.
- `Unmerge` no necesitó cambios: `MergedFrom` ya guarda la snapshot completa de cada
  `ColorGroup` (incluyendo su `IsExcluded` de antes del merge), así que deshacer un
  merge restaura el estado de exclusión original de cada grupo sin lógica adicional.

### 3. Heurística de fondo dominante (ambigüedad de spec.md, resuelta y documentada)

**Criterio elegido:** el grupo con la **mayor área** (ya es `groups[0]`, porque
`detect_palette` devuelve los grupos ordenados por área descendente) se PRE-marca
`IsExcluded=true` **solo si además** cubre **≥50% del perímetro de la imagen**
(`_BACKGROUND_BORDER_TOUCH_RATIO = 0.5` en `color_palette_pipeline.py`).

- `_border_touch_ratio(mask)` (Python, nuevo): perímetro = fila superior + fila
  inferior + columna izquierda + columna derecha, sin contar dos veces las 4
  esquinas. `touches_border = ratio >= 0.5`.
- Por qué 50% y no "toca algún borde": un fondo real típicamente rodea la imagen casi
  por completo; una forma en primer plano que apenas roza una esquina (ej. un
  elemento decorativo cortado por el encuadre) normalmente cubre una fracción mucho
  menor del perímetro. Exigir mayoría absoluta evita falsos positivos.
- Por qué combinarlo con "mayor área" y no usarlo solo: un marco delgado que rodea
  toda la imagen tocaría el 100% del perímetro con un área mínima — no es un "fondo"
  en el sentido que le importa a esta tarjeta (algo grande que el usuario
  probablemente no quiere cortar).
- Es **solo una sugerencia inicial**: el usuario puede revertirla en cualquier
  momento vía `SetExclusionAsync` (test dedicado:
  `SetExclusionAsync_CanRevertTheAutomaticBackgroundSuggestion`).
- Contrato: `touches_border` viaja Python → `PythonColorGroupResult.TouchesBorder` →
  `ColorPaletteService.DetectAsync` (`IsExcluded: groups.Count == 0 && pythonGroup.TouchesBorder`).
  No se expone `touches_border` directamente al frontend (solo el resultado ya
  combinado, `IsExcluded`) porque el criterio completo (mayor área + borde) es
  responsabilidad del backend, no de React.

### 4. Explosión de colores por antialiasing: `tiny_area_ratio`, con evidencia empírica

**Reproducción independiente** (mismo hallazgo que la auditoría M2.1-S01, con un
fixture propio determinista de 300×300, `make_antialiased_illustration_png_bytes` en
`tests/support.py`: "paisaje" de 6 colores sólidos — cielo, pasto, casa, puerta,
techo, sol — con `cv2.LINE_AA` en el techo (triángulo) y el sol (círculo)):

```
ANTES (tiny_area_ratio=0.0, deshabilitado): 18 grupos
  6 colores lógicos: 55.72%, 26.87%, 8.12%, 4.79%, 3.16%, 1.16% del área
  12 grupos "ruido" de antialiasing: entre 0.0089% y 0.0756% del área
```

Hay una separación clara: el color lógico más chico mide 1.1556% del área relevante;
el grupo "ruido" más grande mide 0.0756% — casi 15x de margen entre ambos extremos.

**Umbral elegido: `tiny_area_ratio = 0.001` (0.1%)** — relativo a los píxeles
RELEVANTES (no completamente transparentes), NO al área total del lienzo (así una
imagen con mucho margen transparente no hace que colores opacos legítimos parezcan
"diminutos"). Se probaron varios valores sobre el mismo fixture:

```
tiny_area_ratio=0.0     -> 18 grupos (sin cambios, comportamiento "antes")
tiny_area_ratio=0.0005  -> 10 grupos (mejora parcial, no limpia todo el ruido)
tiny_area_ratio=0.001   -> 6 grupos  (exactamente los 6 colores lógicos) *** elegido ***
tiny_area_ratio=0.005   -> 6 grupos  (igual resultado, confirma margen de seguridad)
tiny_area_ratio=0.01    -> 6 grupos  (igual resultado, confirma margen de seguridad)
```

0.001 separa limpiamente "ruido" (≤0.0756%) de "color lógico" (≥1.1556%) con margen
de ~10x hacia ambos lados, y sigue dando el mismo resultado (6 grupos) hasta 10x más
alto (0.01) — no es un valor "al límite". Es distinto del `tiny_area_ratio` de M2-S03
(0.0005) a propósito: ese umbral protege componentes físicos de corte láser (donde
una pieza chica pero real nunca debe desaparecer en silencio, así que el umbral es
más conservador); este protege contra ruido de antialiasing, un dominio distinto con
evidencia empírica propia.

**Mecanismo** (`_merge_tiny_groups_into_nearest`, nuevo en
`color_palette_pipeline.py`): se aplica ANTES del paso existente de `max_colors`.
Mientras quede más de un cluster vivo y el más chico esté por debajo del umbral, se
fusiona con su vecino de color más cercano en Lab — **sin mirar `tolerance`**: un
cluster de 3 píxeles a distancia 40 en Lab de su vecino sigue siendo ruido de
antialiasing que hay que absorber, no un color legítimo que preservar separado solo
porque nadie más quedó tan cerca. Determinista (empates resueltos por índice
ascendente), nunca colapsa a 0 grupos (`while len(live) > 1`).

**Opcional/configurable** (requisito explícito de spec.md: "no debe impedir que un
usuario avanzado pida explícitamente muchos colores finos"): `tiny_area_ratio` es un
parámetro más de `ColorPaletteParams`/`ColorPaletteParameters`, con el mismo patrón
que `tolerance`/`max_colors` — validado en rango `[0, 0.5]`
(`ColorPaletteOptions.MinTinyAreaRatio`/`MaxTinyAreaRatio`), resuelto a
`DefaultTinyAreaRatio=0.001` cuando se omite. Pasar `0.0` explícitamente lo
deshabilita por completo (test
`test_detect_palette_tiny_area_ratio_can_be_disabled_explicitly_for_fine_grained_palettes`).
No se expuso como control nuevo en el panel de React (spec.md solo pide el toggle
incluir/excluir del lado de frontend para esta tarjeta; el default de producción ya
resuelve el caso común, y el parámetro queda disponible en la API para quien lo
necesite).

### 5. Casos límite — cobertura explícita

Todos con test dedicado (`test_color_palette_pipeline.py`, `test_color_palette_service.py`, `test_color_palette_route.py`):

- **Gradientes**: `test_detect_palette_gradient_with_default_style_tuning_stays_bounded_and_does_not_collapse`
  — degradé continuo de 200 columnas, `1 < len(groups) < 200` con la tolerancia +
  `tiny_area_ratio` de producción (no explota a "una por columna", no colapsa a 1).
- **Colores muy próximos**: ya cubierto por M2-S01, reverificado sin cambios.
- **Transparencia**: reverificado con `tiny_area_ratio` activo
  (`test_detect_palette_transparency_case_is_unaffected_by_tiny_area_ratio`,
  `test_process_partial_transparency_does_not_fail_and_is_flagged`, etc.) — el grupo
  opaco no se fusiona ni desaparece, los píxeles transparentes se siguen excluyendo.
- **Fondo dominante**: `test_detect_palette_marks_the_group_covering_most_of_the_border_as_touching_it`,
  `test_detect_palette_interior_group_never_touches_border`,
  `test_process_dominant_background_group_is_flagged_touches_border` (service),
  `DetectAsync_PreMarksTheLargestGroupThatTouchesTheBorderAsExcluded` (.NET).
- **Imagen B/N (2 colores), regresión explícita**:
  `test_detect_palette_black_and_white_two_colors_is_unaffected_by_the_antialiasing_fix`,
  `test_process_black_and_white_two_colors_regression` — sigue devolviendo
  exactamente 2 grupos de 50/50 con el fix activo.
- **`maxColors` > colores reales**: `test_detect_palette_max_colors_greater_than_actual_colors_returns_actual_count_without_error`,
  `test_process_max_colors_greater_than_actual_colors_returns_actual_count` — devuelve
  la cantidad real (3), sin error ni colores inventados.

### 6. Estabilidad de IDs durante la edición — test dedicado

`GroupId_OfAnUntouchedGroup_RemainsStableAcrossSuccessiveRenameAndExclusionEdits`
(.NET, `ColorPaletteServiceTests.cs`): detecta, renombra un grupo, excluye/incluye
OTRO grupo, y confirma en cada paso que el `GroupId`, `Name`, `IsExcluded` y
`ColorHex` del grupo NO tocado se mantienen exactamente iguales. Confirma que
`SortGroups` (reordena por área/nombre, pero nunca reasigna `GroupId`) preserva la
identidad de cada grupo a través de ediciones sucesivas, tal como ya lo garantizaba
el diseño de M2-S01 — ahora verificado explícitamente, no asumido.

### 7. Contrato

Se mantuvo `ColorPaletteResponse`/`ColorGroupPayload` (no se adoptó el nombre literal
`PaletteResult`/`colors[]`): el contrato actual ya cumple el resultado esperado de
spec.md (id estable, color, cobertura, estado incluido/excluido). Cambios aditivos:

- `ColorGroupPayload.IsExcluded: bool` (nuevo, requerido por esta tarjeta).
- `ColorGroupPayload.Rgb: { R, G, B }` (nuevo, aditivo — sugerido explícitamente por
  spec.md junto a `ColorHex` ya existente; calculado parseando el hex ya validado, sin
  tocar el modelo persistido).
- `ColorPaletteResponse.TinyAreaRatio: double` (nuevo, eco del parámetro efectivo,
  mismo patrón que `Tolerance`/`MaxColors`).
- Python: `ColorGroupPayload.touches_border: bool`, `ColorPaletteParams.tiny_area_ratio: float`.

Nada de lo ya consumido por M2-S01/M2-S02/M2-S05/M2.1-S01 se quitó ni se renombró.

### 8. Archivos creados

- `backend/Vectorify.Api/Contracts/ColorPaletteSetExclusionRequest.cs`

### 9. Archivos modificados

**Python (`services/python-engine/`):**
- `app/core/color_palette_pipeline.py` — `ColorGroup.touches_border`, `_border_touch_ratio`,
  `_merge_tiny_groups_into_nearest`, `_merge_down_to_max_colors` (acepta `live`/`union_find`
  iniciales para encadenar con el paso de limpieza), `detect_palette(..., tiny_area_ratio=0.0)`.
- `app/core/config.py` — `color_palette_default_tiny_area_ratio` y rango (documentación/referencia,
  mismo criterio no-wireado que `color_palette_default_tolerance`).
- `app/models/schemas.py` — `ColorPaletteParams.tiny_area_ratio`, `ColorGroupPayload.touches_border`.
- `app/services/color_palette_service.py` — pasa `params.tiny_area_ratio` a `detect_fn`, mapea
  `touches_border` al payload.
- `tests/support.py` — `make_bw_two_color_png_bytes`, `make_dominant_background_png_bytes`,
  `make_antialiased_illustration_png_bytes` (fixture de reproducción/evidencia).
- `tests/test_color_palette_pipeline.py` — 13 tests nuevos (tiny_area_ratio, touches_border, casos límite).
- `tests/test_color_palette_service.py` — 5 tests nuevos (extremo a extremo).
- `tests/test_color_palette_route.py` — 1 assertion actualizada (`touches_border` en el contrato) + 2 tests nuevos.

**Backend (`backend/Vectorify.Api/`):**
- `ColorPalette/ColorGroup.cs` — `IsExcluded: bool`.
- `ColorPalette/ColorPaletteParameters.cs` — `TinyAreaRatio: double`, `ToCacheKey` actualizado.
- `ColorPalette/ColorPaletteService.cs` — heurística de fondo dominante en `DetectAsync`, herencia
  de `IsExcluded` en `MergeAsync`, nuevo método `SetExclusionAsync`.
- `ColorPalette/IColorPaletteService.cs` — firma de `SetExclusionAsync`.
- `ColorPalette/ColorPaletteParameterValidator.cs` — valida `TinyAreaRatio`.
- `Options/ColorPaletteOptions.cs` — `DefaultTinyAreaRatio`/`MinTinyAreaRatio`/`MaxTinyAreaRatio`.
- `Contracts/ColorPaletteDetectRequest.cs` — `TinyAreaRatio` opcional.
- `Contracts/ColorPaletteResponse.cs` — `RgbColor`, `ColorGroupPayload.Rgb`/`IsExcluded`, `ColorPaletteResponse.TinyAreaRatio`.
- `Contracts/PythonColorPalettePayload.cs` — `touches_border`, `tiny_area_ratio`.
- `Clients/PythonColorPaletteResult.cs` — `PythonColorGroupResult.TouchesBorder`.
- `Clients/PythonColorPaletteClient.cs` — envía `tiny_area_ratio`, lee `touches_border`.
- `Endpoints/ColorPaletteEndpoints.cs` — endpoint `POST .../exclude`, `ToRgb`, mapeos actualizados.

**Backend tests:**
- `ColorPalette/ColorPaletteServiceTests.cs` — 9 tests nuevos (heurística de fondo, `SetExclusionAsync`,
  herencia de `IsExcluded` en merge, estabilidad de IDs).
- `ColorPalette/ColorPaletteParameterValidatorTests.cs` — 3 métodos nuevos (`TinyAreaRatio`), 6 casos en total.
- `Clients/PythonColorPaletteClientTests.cs` — 2 tests nuevos (`touches_border`, envío de `tiny_area_ratio`).
- `ColorPalette/FakePythonColorPaletteClient.cs`, `VectorLayers/VectorLayerServiceTests.cs`,
  `ColorPalette/PersistentColorPaletteVersionRegistryTests.cs`, `TestSupport/ColorPalettePayloads.cs` —
  actualizados para el nuevo parámetro posicional (`TouchesBorder`) de `PythonColorGroupResult` y
  (`TinyAreaRatio`) de `ColorPaletteParameters`.

**Frontend (`frontend/src/`):**
- `types/colorPalette.ts` — `RgbColor`, `ColorGroupPayload.rgb`/`isExcluded`, `ColorPaletteResponse.tinyAreaRatio`.
- `api/colorPaletteApi.ts` — `setColorPaletteGroupExclusion`.
- `hooks/useColorPalette.ts` — `setExclusion`.
- `components/colorPalette/ColorSwatchList.tsx` — toggle incluir/excluir por swatch (checkbox,
  `aria-label="Excluir {name} del corte"`).
- `components/colorPalette/ColorPalettePanel.tsx` — conecta `setExclusion` al swatch list.
- `App.css` — estilos `.color-swatch__exclusion`.
- `components/colorPalette/ColorPalettePanel.test.tsx` — fixtures actualizados (`rgb`, `isExcluded`,
  `tinyAreaRatio`) + 3 tests nuevos (fondo pre-excluido visible, marcar/desmarcar exclusión).

### 10. Cobertura de los criterios de aceptación ampliados de spec.md

- [x] Flag incluido/excluido por color, independiente de merge, versionado.
- [x] Detección de fondo dominante (mayor área + toca ≥50% del perímetro), pre-marca sugerida, revertible por el usuario.
- [x] Explosión de colores por antialiasing atacada (`tiny_area_ratio`), con evidencia antes/después (18→6 grupos), opcional/configurable.
- [x] Casos límite con tests deterministas (gradientes, colores próximos, transparencia, fondo dominante, B/N, maxColors > reales).
- [x] Estabilidad de `GroupId` verificada con test dedicado.
- [x] Contrato: `ColorGroupPayload`/`ColorPaletteResponse` mantenidos, `Rgb`/`IsExcluded`/`TinyAreaRatio` agregados de forma aditiva.
- [x] Frontend: toggle incluir/excluir agregado al panel ya existente (`ColorPalettePanel.tsx`/`ColorSwatchList.tsx`), sin panel paralelo.
- [x] Fuera de alcance respetado: no se tocó edición manual de paths ni generación por IA.

### 11. Verificación — resultados exactos de los 4 suites (completos, no solo nuevos)

- **`dotnet build Vectorify.sln`** → Compilación correcta, 0 advertencias, 0 errores.
- **`dotnet test Vectorify.sln`** → `Correctas! - Con error: 0, Superado: 622, Omitido: 0, Total: 622` (605 preexistentes + 17 nuevos: 9 en `ColorPaletteServiceTests` + 6 casos en `ColorPaletteParameterValidatorTests` (1 `Fact` + 2 `Theory` con 2 y 3 `InlineData` respectivamente) + 2 en `PythonColorPaletteClientTests`).
- **`pytest`** (`services/python-engine`, entorno virtual local `.venv`) → `382 passed, 1 warning in ~8s` (362 preexistentes + 20 nuevos: 13 en `test_color_palette_pipeline.py` + 5 en `test_color_palette_service.py` + 2 en `test_color_palette_route.py`, más 1 assertion de contrato existente actualizada). El warning es preexistente (`starlette`, no relacionado).
- **`npm run build`** (frontend, `tsc -b && vite build`) → build y typecheck OK, sin errores.
- **`npm run lint`** (oxlint) → sin hallazgos, exit code 0.
- **`npm test`** (vitest) → `Test Files 23 passed (23)`, `Tests 180 passed (180)` (177 preexistentes + 3 nuevos). Una corrida aislada de la suite completa mostró 1 falla intermitente en `ExplodedView.test.tsx` (módulo de M2-S03, no tocado en este sprint) que desapareció al re-correr esa suite sola y al re-correr la suite completa de nuevo (180/180) — confirmado como flaky preexistente ajeno a este cambio (`git diff --stat frontend/src/components/layers/` no muestra ningún archivo tocado).

### Supuestos y decisiones no triviales

- **Heurística de fondo dominante**: mayor área + ≥50% del perímetro tocado — ver sección 3.
- **Umbral `tiny_area_ratio=0.001`**: elegido con evidencia empírica (18→6 grupos, margen ~10x) — ver sección 4.
- **`IsExcluded` en merge**: hereda `true` solo si TODOS los grupos fusionados ya estaban excluidos — ver sección 2.
- **Nombre del flag**: se usó `IsExcluded` (spec.md ofrecía `IsExcluded`/`IsBackground` como sinónimos) — un único
  booleano cubre ambos sentidos (la detección automática es solo quien lo PRE-marca; el significado para el
  usuario es siempre "excluido del corte").
- **`tiny_area_ratio` no se expuso como control nuevo en el panel de React**: spec.md solo pide el toggle
  incluir/excluir del lado de frontend para esta tarjeta; el parámetro sí es controlable vía API para quien lo necesite.
- **`touches_border` no se expone directamente en el contrato de React**: solo el resultado ya combinado
  (`IsExcluded`) — el criterio completo es responsabilidad del backend.

No se tocó `.claude/`. No se hizo commit, push, merge ni se tocó Notion.
