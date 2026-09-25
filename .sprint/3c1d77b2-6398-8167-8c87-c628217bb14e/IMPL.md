status: ok

## M1-S09 · Dimensiones reales en milímetros

Implementa la conversión de un SVG ya generado (vectorizado o simplificado)
a dimensiones físicas explícitas en milímetros: el usuario define ancho O
alto (proporción bloqueada, default — el otro se calcula) o ambos
(proporción desbloqueada — permite deformar el diseño explícitamente), ve un
preview del tamaño final calculado 100% en el cliente, y "Aplicar" persiste
el resultado reescribiendo SOLO los atributos `width`/`height`/`viewBox`/
`preserveAspectRatio` del elemento raíz `<svg>` — nunca los `d` de los
`<path>`. **No se tocó Python en absoluto**: toda la operación es
aritmética de escala + reescritura de metadata XML, implementada
íntegramente en `Vectify.Api` (ver decisión más abajo).

---

Archivos (Backend — `backend/Vectify.Api`):
- `Dimensioning/*` (13 archivos, módulo propio y paralelo a
  `Simplification/`/`Checking/`): `DimensionSourceKind` (enum `Vector` |
  `Simplification`, mismo criterio dual que M1-S08), `DimensionRequestParameters`
  (forma cruda: sourceKind + widthMm?/heightMm? + lockAspectRatio, todavía
  sin resolver contra el SVG de origen), `DimensionParameters` (forma final:
  WidthMm/HeightMm ya resueltos + LockAspectRatio, con `ToCacheKey()`),
  `DimensionRequestValidationResult`/`DimensionParameterValidationResult`,
  `IDimensionParameterValidator`/`DimensionParameterValidator` (validación en
  DOS pasos — ver decisión de diseño), `SvgDimensionWriter` (núcleo puro:
  reescribe metadata del `<svg>` raíz vía `System.Xml.Linq.XDocument`, SIN
  tocar ningún `d`), `InvalidDimensionSourceSvgException`, `DimensionVersion`
  (nuevo tipo de versión persistida, ver decisión), `DimensionResult` (unión
  `Ready`/`NotFound`/`ValidationFailed`/`UpstreamError`, mismo patrón que
  `SimplificationResult`), `IDimensionVersionRegistry`/
  `InMemoryDimensionVersionRegistry`/`PersistentDimensionVersionRegistry`
  (sidecar JSON atómico en `App_Data/dimensions/{ProjectId}/{ImageId}/{DimensionId}.json`,
  rehidratación al arrancar — mismo patrón exacto que
  `PersistentSimplificationVersionRegistry`), `IDimensionService`/
  `DimensionService` (localiza el SVG de origen vía
  `IVectorizationService.FindVector`/`ISimplificationService.FindSimplification`,
  reescribe con `SvgDimensionWriter`, cachea+versiona+lockea por clave — SIN
  ninguna llamada HTTP a Python de por medio).
- `Contracts/DimensionRequest.cs`, `DimensionResponse.cs`.
- `Endpoints/DimensionEndpoints.cs` — `POST .../dimensions/apply` (201 si es
  nueva versión, 200 si es cache-hit) y `GET .../dimensions/{id}` (sirve los
  bytes del SVG ya dimensionado, para el flujo de round-trip).
- `Options/DimensionOptions.cs` (rango `[MinMm, MaxMm]`, default 1–1000),
  `Options/DimensionRegistryOptions.cs` (ruta del sidecar JSON).
- `Program.cs` (mod) — wiring de DI/opciones y `app.MapDimensionEndpoints()`.
- Tests: `Dimensioning/SvgDimensionWriterTests.cs` (14 tests: síntesis de
  `viewBox` cuando falta, preservación cuando ya existe, `width`/`height`
  con unidad `mm` explícita, `preserveAspectRatio="none"` solo cuando
  deforma, nunca toca `d`, aspect ratios 1:1/muy ancho/muy alto, round-trip,
  determinismo, SVG malformado/sin raíz `<svg>` → excepción tipada),
  `Dimensioning/DimensionParameterValidatorTests.cs` (23 tests: sourceId/
  sourceKind, exclusividad ancho/alto según lock, rango `[1,1000]` inclusive
  en ambos extremos, derivación correcta para 1:1/muy ancho/muy alto, valor
  derivado fuera de rango en ambos sentidos), `Dimensioning/DimensionServiceTests.cs`
  (13 tests: NotFound por fuente, validación de dos pasos, primera vez
  persiste, nunca sobrescribe el SVG de origen, cache-hit avanza versión sin
  duplicar storage, concurrencia, `preserveAspectRatio` solo al deformar,
  `FindDimension`), `Dimensioning/PersistentDimensionVersionRegistryTests.cs`
  (5 tests: guardar/buscar, sidecar en disco, sobrevive a "reinicio"),
  `Dimensioning/FakeVectorizationService.cs`/`FakeSimplificationService.cs`,
  `EndToEnd/DimensionEndpointsTests.cs` (18 tests: pipeline completo
  upload→...→vector→dimensions/apply, también sobre una simplificación
  aplicada, aspect ratios 1:1/muy ancho/muy alto vía HTTP, round-trip real
  — GET del SVG persistido y verificación matemática viewBox↔mm—,
  deformación con `preserveAspectRatio="none"`, cache-hit avanza versión,
  valores límite 0/negativo/por-encima-del-máximo/en-el-borde, ambos
  provistos con lock → 400, uno solo sin lock → 400, sourceKind desconocido
  → 400, sourceId inexistente → 404, dimensionId inexistente → 404).

Archivos (Frontend — `frontend/src`):
- `types/dimension.ts` — `DimensionSourceKind`, `DimensionResponse`,
  `DimensionErrorCode` (reflejan los contratos de C#).
- `api/dimensionApi.ts` — `applyDimensions` (único llamado HTTP de todo el
  flujo) y `getDimensionedSvgUrl`.
- `lib/dimensionScale.ts` (+ `dimensionScale.test.ts`, 18 tests) —
  `computeDimensionPreview`: réplica en TypeScript, puramente en memoria, de
  `DimensionParameterValidator.ResolveDimensions` — mismos rangos
  `[MIN_DIMENSION_MM, MAX_DIMENSION_MM]` = `[1, 1000]`, misma lógica de
  exclusividad/derivación. `parseMmInput` (string de input → número o
  `null`).
- `hooks/useDimensions.ts` — estados `idle/applying/applied/error`; el
  preview NO tiene un estado propio (se recalcula en cada render, sin
  llamada de red); `apply()` es la única función que llama a la Web API;
  cambiar de fuente (sourceKind/sourceId) invalida el resultado "aplicado"
  vigente (mismo criterio que `useCheck`), pero conserva lo que el usuario
  ya escribió en los inputs.
- `components/dimensions/DimensionControls.tsx` — dos inputs numéricos
  (`Ancho (mm)`/`Alto (mm)`) + checkbox "Proporción bloqueada", con hint de
  texto que explica el comportamiento vigente.
- `components/dimensions/DimensionPanel.tsx` (+ test, 9 casos) — orquesta
  selección de fuente (vector actual / última simplificación, mismo patrón
  que `CheckPanel`) → controles → preview local → "Aplicar" → confirmación
  con link al SVG persistido.
- `App.tsx`/`App.css` (mod) — encadena la sección "Dimensiones físicas" tras
  un vector listo (mismo nivel del pipeline que el Laser Checker), pie de
  página actualizado a M1-S09.

Dependencias agregadas: ninguna (`System.Xml.Linq` es BCL, ya usado por
`PythonVectorizeClient`/`PythonSimplifyClient`; nada nuevo del lado
frontend).

Verificación (corrida de verdad):
- Backend: `dotnet build` → 0 errores, 0 warnings. `dotnet test` → **352/352
  pasaron** (279 base + 73 nuevos: 14 `SvgDimensionWriterTests` + 23
  `DimensionParameterValidatorTests` + 13 `DimensionServiceTests` + 5
  `PersistentDimensionVersionRegistryTests` + 18 `DimensionEndpointsTests`).
- Python: no se tocó ningún archivo de `services/python-engine` (confirmado
  con `git diff --stat`); no se pudo correr `pytest` en este sandbox (no hay
  intérprete de Python disponible — `python`/`py`/`python3` no encontrados),
  pero al no haber ningún cambio en ese árbol el resultado debería seguir en
  **211/211** exacto respecto a `main`.
- Frontend: `tsc -b && vite build` → OK. `oxlint` → sin hallazgos. `npm test`
  (vitest) → **111/111 pasaron** (84 base + 27 nuevos: 18
  `dimensionScale.test.ts` + 9 `DimensionPanel.test.tsx`). Nota: en una
  corrida se observaron 5 timeouts intermitentes (`SimplifyPanel`,
  `DimensionPanel`, `UploadPanel`, `PreprocessPanel`) por carga del sistema
  — no reproducibles, una segunda corrida completa pasó 111/111 sin
  cambios; mismo patrón de flakiness ya documentado en el reporte de M1-S08
  sobre `ThresholdPanel.test.tsx`.

## Decisiones de diseño y supuestos

- **Todo en C#, CERO llamadas a Python**: aplicar dimensiones físicas es
  puramente reescribir `width`/`height`/`viewBox`/`preserveAspectRatio` del
  elemento raíz `<svg>` — no hay ningún cálculo de imagen ni geometría
  compleja involucrado (nunca se tocan los `d` de los `<path>`). Se evaluó
  seguir el patrón simétrico de las demás etapas (Python calcula, .NET
  orquesta) pero se descartó: hubiera significado agregar un endpoint FastAPI
  cuyo "cálculo" es una división y una interpolación de string, sin ninguna
  ventaja (ni reutilización de OpenCV/vtracer, ni un algoritmo que valga la
  pena encapsular detrás de una interfaz de motor). `SvgDimensionWriter` usa
  `System.Xml.Linq.XDocument` — la misma librería que
  `PythonVectorizeClient`/`PythonSimplifyClient` ya usan del lado .NET para
  validar defensivamente el SVG que devuelve Python — así que no se introduce
  ningún patrón nuevo, solo se reutiliza para escribir en vez de solo leer.
- **Tipo de versión elegido: `DimensionVersion`**, con su propio registro
  (`IDimensionVersionRegistry`/`PersistentDimensionVersionRegistry`) —
  exactamente el nombre sugerido por el spec, siguiendo el mismo patrón de
  historial versionado inmutable que `SimplificationVersion`/`VectorVersion`
  (nunca se sobrescribe; cache-hit sobre el mismo SVG de origen + las mismas
  dimensiones avanza la versión en vez de retroceder). A diferencia de
  Simplification, el registro NO expone `FindLatest` (nada en este sprint lo
  necesita: el frontend siempre tiene el `dimensionId` recién creado a mano)
  — omitido por YAGNI, documentado explícitamente en el propio archivo de la
  interfaz.
- **Preview 100% en el cliente, sin round-trip HTTP**: dado que es
  aritmética de escala pura (no hay ningún motor externo cuyo resultado no
  se pueda anticipar en el navegador), NO existe un endpoint
  `.../dimensions/preview`. `frontend/src/lib/dimensionScale.ts` replica en
  TypeScript la misma lógica que
  `DimensionParameterValidator.ResolveDimensions` del lado C# (mismos rangos
  `[1, 1000]mm`, misma derivación del valor faltante), y se recalcula en cada
  render sin ningún `useEffect`/debounce. La Web API sigue siendo la ÚNICA
  fuente de verdad de lo que se persiste: "Aplicar" manda el valor TAL COMO
  el usuario lo completó (no el derivado por React), y
  `DimensionParameterValidator.ResolveDimensions` (backend) vuelve a
  calcular y validar todo desde cero contra el aspect ratio REAL del SVG de
  origen — si el preview local y el backend llegaran a discrepar por algún
  motivo (ej. reglas de negocio que cambien de un lado y no del otro), el
  usuario vería el resultado correcto recién al aplicar, nunca uno
  inconsistente persistido.
- **Unidad interna documentada** (spec.md, "Reglas"): 1 unidad de las
  coordenadas del SVG generado por este pipeline (`viewBox`, y los puntos
  dentro de cada `d`) equivale a 1 píxel de la máscara binaria B/N (M1-S04)
  que VTracer vectorizó — nunca se asume ningún DPI/PPI. Verificado
  leyendo `services/python-engine/app/services/vectorization_service.py`:
  `VectorizeResponse.Width`/`Height` (y por herencia
  `SimplificationVersion.Width`/`Height`, que nunca cambian tras simplificar)
  se reportan directamente desde `mask.shape` (dimensiones en píxeles de la
  máscara ya decodificada), NO desde ningún metadato EXIF/DPI del raster
  original — coherente con la decisión ya tomada en M1-S02 de no confiar en
  esa metadata. El factor de escala mm↔unidad interna se deriva así:
  `scaleX = widthMm / sourceWidthPx`, `scaleY = heightMm / sourceHeightPx`
  (implícito: nunca se aplica a ningún punto, solo se usa conceptualmente
  para entender qué representan los atributos finales `width="{mm}mm"` /
  `height="{mm}mm"` frente al `viewBox` sin cambiar).
- **Validación en DOS pasos** (`IDimensionParameterValidator.ValidateRequest`
  + `.ResolveDimensions`), a diferencia del `Validate` único de
  Simplification/Check: el valor faltante en modo bloqueado depende del
  aspect ratio REAL del SVG de origen, que `DimensionService` recién conoce
  después de localizarlo (`ResolveSource`) — no se puede resolver/validar
  todo de una sola pasada sin acoplar el validador a la resolución de
  storage. Paso 1 (`ValidateRequest`): sourceId/sourceKind presentes,
  exclusividad ancho/alto según `LockAspectRatio`, rango de cada valor
  INFORMADO. Paso 2 (`ResolveDimensions`, ya con el SVG de origen resuelto):
  deriva el valor faltante si está bloqueada, y vuelve a validar en rango el
  resultado FINAL — un ancho válido puede derivar un alto fuera de rango si
  el aspect ratio de la fuente es muy extremo (cubierto por tests).
- **Rango de mm permitido: `[1, 1000]`, inclusive en ambos extremos**
  (spec.md no lo cuantifica, supuesto documentado en
  `Vectify.Api.Options.DimensionOptions`): 1mm de mínimo (por debajo deja de
  ser una medida físicamente útil para fabricación, y evita acercarse a una
  división por cero al despejar el lado bloqueado del aspect ratio), 1000mm
  de máximo (1 metro: compatible con el área de trabajo de una cortadora
  láser de escritorio típica — una pieza más grande normalmente se resuelve
  con nesting/paneles, fuera de alcance explícito de esta tarjeta). A
  diferencia de las tolerancias relativas `(0, max]` de Simplification/Check,
  acá el rango es inclusivo en AMBOS extremos porque 1mm y 1000mm son
  valores límite legítimos según spec.md ("Pruebas": "valores límite
  (mínimo/máximo permitido...)"), no umbrales donde 0 sea un caso especial
  sin sentido.
- **`preserveAspectRatio="none"` solo cuando `LockAspectRatio=false`**: con
  la proporción bloqueada, ancho/alto en mm ya guardan la misma proporción
  que el `viewBox` por construcción, así que el comportamiento default de
  SVG (`xMidYMid meet`, que preserva aspecto) no tiene ningún efecto visible
  y se omite el atributo. Con la proporción desbloqueada, el usuario pidió
  explícitamente poder deformar el diseño (spec.md: "esto SÍ deforma el
  diseño... permitido explícitamente") — sin `preserveAspectRatio="none"`,
  un visor SVG conforme al estándar centraría el contenido dentro del
  `width`/`height` pedido y dejaría márgenes vacíos en vez de estirarlo, lo
  que contradiría el pedido explícito del usuario.
- **`viewBox` sintetizado solo si falta**: los SVG que emite VTracer en este
  pipeline (`services/python-engine/app/core/vector_engine.py`) no incluyen
  un `viewBox` explícito, solo `width`/`height` sin unidad — verificado
  contra los fixtures de test existentes
  (`VectorizePayloads`/`SimplifyPayloads`). `SvgDimensionWriter` agrega
  `viewBox="0 0 {sourceWidthPx} {sourceHeightPx}"` únicamente cuando el
  atributo no existe (usando `VectorVersion.Width`/`Height`, ya validados
  como fuente de verdad por el resto del pipeline, en vez de re-derivarlos
  del propio XML); si el SVG de origen YA trae un `viewBox` (ej. de un
  sprint futuro que lo agregue), se preserva sin tocar.

## Nota de revisión (orquestador)

El implementador no agregó las secciones `Dimensions`/`DimensionsRegistry` a
`appsettings.json`, a diferencia de todas las etapas anteriores (que sí las
documentan ahí aunque los valores coincidan con los defaults de código).
Agregadas en la revisión, sin cambio funcional (`DimensionOptions`/
`DimensionRegistryOptions` ya tenían esos defaults en C#, el binding de
configuración simplemente no encontraba la sección y usaba los defaults de
todos modos) — solo por consistencia/descubribilidad con el resto del
proyecto. Verificado `dotnet build`/`dotnet test` después del cambio, sin
regresiones (352/352). También verifiqué independientemente (no solo
confiando en el reporte): `dotnet test` 352/352, `pytest` 211/211 (sí pude
correrlo, a diferencia del sandbox del implementador), `npm test` 111/111
sin flakiness en mi corrida. Revisé a mano `DimensionParameterValidator.cs`,
`SvgDimensionWriter.cs`, `DimensionService.cs` y comparé la aritmética de
`dimensionScale.ts` contra `ResolveDimensions` del backend línea por línea:
coinciden exactamente. No encontré bugs en esta ronda.

## Excepciones/limitaciones conocidas

- Heredado de tarjetas anteriores: sin verificación en `docker compose up
  --build` ni navegador real (fuera del ciclo local de build/test).
- Kerf/material/máquina y nesting: fuera de alcance explícito de spec.md, no
  implementados.
- `services/python-engine` no fue tocado y `pytest` no se pudo ejecutar en
  este entorno (sin intérprete de Python disponible) -- verificado por
  ausencia total de cambios en ese árbol (`git diff --stat -- services/python-engine`
  vacío), así que no hay riesgo de regresión ahí.
- El preview client-side (`lib/dimensionScale.ts`) es una aproximación de UX
  que debe mantenerse manualmente sincronizada con
  `DimensionParameterValidator`/`DimensionOptions` del lado C# (mismos
  rangos `[1, 1000]mm`, misma fórmula de derivación) -- no hay un mecanismo
  automático que las mantenga en sync entre ambos lenguajes. Si un sprint
  futuro cambia el rango permitido del lado backend, hay que actualizar
  `MIN_DIMENSION_MM`/`MAX_DIMENSION_MM` del lado frontend a mano. Riesgo
  acotado: aunque discreparan, el backend sigue siendo la única fuente de
  verdad de lo que se persiste (ver decisión de diseño arriba).
- Test observado como intermitente en una corrida completa del frontend
  (`SimplifyPanel.test.tsx`, `DimensionPanel.test.tsx`, `UploadPanel.test.tsx`,
  `PreprocessPanel.test.tsx`: 5 timeouts de 5000ms) bajo carga del sistema;
  una segunda corrida completa inmediata pasó 111/111 sin cambios -- mismo
  patrón ya documentado en el reporte de M1-S08 (`ThresholdPanel.test.tsx`),
  no relacionado con ningún archivo específico de este sprint.
