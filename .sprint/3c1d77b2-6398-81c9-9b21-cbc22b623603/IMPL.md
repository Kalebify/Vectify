status: ok

## M1-S10 · Exportación SVG

Cierra el primer flujo productivo: el usuario elige QUÉ versión exportar —
una `VectorVersion` (M1-S05), una `SimplificationVersion` (M1-S07) o una
`DimensionVersion` (M1-S09), extendiendo a un tercer valor el mismo patrón
dual ya usado en Checking/Dimensioning (M1-S08/M1-S09) — ve un resumen
(versión, tamaño en mm o px, issues del Laser Checker) y descarga el
archivo. El endpoint de export **NO modifica geometría**: sirve exactamente
los mismos bytes ya persistidos por la etapa de origen, con
`Content-Disposition: attachment` y un nombre de archivo sanitizado (RFC
6266, dos partes). **No se tocó Python en absoluto**: es un GET de solo
lectura sobre storage, sin ningún motor externo de por medio.

---

Archivos (Backend — `backend/Vectify.Api`):
- `Export/ExportSourceKind.cs` — enum `Vector | Simplification | Dimension`,
  tercer valor que extiende el patrón dual de `CheckSourceKind`/
  `DimensionSourceKind`.
- `Export/ExportFileNameSanitizer.cs` — función pura y testeada que deriva
  un nombre de descarga seguro a partir de `ProjectRecord.FileName` (el
  nombre original que el usuario subió en M1-S02, sin restricciones): quita
  cualquier componente de directorio, quita la extensión original,
  reemplaza por `_` los caracteres reservados de Windows (`< > : " / \ | ? *`)
  y los caracteres de control, recorta puntos/espacios de los bordes,
  trunca a 80 caracteres, prefija con `_` los nombres reservados del sistema
  (`CON`, `PRN`, `COM1`...), y cae a `"export"` si el resultado queda vacío
  o no hay `ProjectRecord` disponible. Preserva tildes, espacios y símbolos
  benignos (paréntesis, `#`, `&`, etc.) — NO se ocupa del encoding
  ASCII/UTF-8 del header (ver decisión más abajo).
- `Export/ExportResult.cs` — unión `Ready | NotFound | ValidationFailed`
  (sin `UpstreamError`: no hay ningún motor externo que pueda fallar de
  forma controlada, a diferencia de Checking/Dimensioning).
- `Export/IExportService.cs`/`ExportService.cs` — valida sourceKind/
  sourceId, localiza el SVG de origen YA generado vía
  `IVectorizationService.FindVector`/`ISimplificationService.FindSimplification`/
  `IDimensionService.FindDimension` (según sourceKind), busca el
  `ProjectRecord` vía `IProjectRegistry.Find` para el nombre base, deriva el
  nombre de archivo, y registra el log estructurado de auditoría (ver
  decisión). **Sin caché/lock/registro versionado propio** (mismo criterio
  YAGNI que Checking, M1-S08): este servicio nunca genera ni persiste nada,
  solo referencia bytes ya existentes.
- `Endpoints/ExportEndpoints.cs` — `GET .../export?sourceKind=vector|simplification|dimension&sourceId={guid}`:
  abre el stream de storage, arma el header `Content-Disposition` con
  `Microsoft.Net.Http.Headers.ContentDispositionHeaderValue.SetHttpFileName`
  (RFC 6266/5987: `filename="..."` ASCII de respaldo +
  `filename*=UTF-8''...` con el nombre real) y devuelve `Results.Stream`.
- `Program.cs` (mod) — wiring de `IExportService` (Scoped) y
  `app.MapExportEndpoints()`.
- Tests: `Export/ExportFileNameSanitizerTests.cs` (17 tests: tildes/espacios/
  símbolos preservados, caracteres reservados de Windows reemplazados,
  separadores de ruta descartados, solo se quita la ÚLTIMA extensión,
  caracteres de control, nombre nulo/vacío/solo-espacios → fallback,
  resultado vacío tras sanitizar → fallback, recorte de puntos/espacios
  finales, nombres reservados de Windows → prefijo `_`, truncamiento a 80
  caracteres, sin extensión, composición final y determinismo),
  `Export/ExportServiceTests.cs` (15 tests: sourceId vacío/sourceKind
  desconocido → ValidationFailed, parseo case-insensitive y con espacios,
  resolución correcta de storageKey/contentType/version para los tres
  sourceKind, `NotFound` para cada uno, nombre derivado del `FileName`
  original + sourceKind + versión, fallback cuando no hay `ProjectRecord`,
  determinismo, nunca invoca `GenerateVectorAsync`/`PreviewAsync`/
  `ApplyAsync`/`ApplyAsync` de los servicios de origen — los fakes lanzan si
  se llegan a invocar), `Export/FakeVectorizationService.cs`/
  `FakeSimplificationService.cs`/`FakeDimensionService.cs`/
  `FakeProjectRegistry.cs` (fakes en memoria, mismo patrón que
  `Dimensioning.Tests`/`Checking.Tests`), `EndToEnd/ExportEndpointsTests.cs`
  (18 tests: pipeline completo upload→...→vector→export y también sobre una
  simplificación y sobre una dimensión aplicadas; **round-trip byte a
  byte** — compara los bytes exportados contra los bytes servidos por el
  endpoint de la etapa de origen, deben ser IDÉNTICOS; nombres con
  caracteres especiales — tildes, espacios, `#`, `%`, `&` — verificando
  `ContentDisposition.FileNameStar`/`FileName` tipados; export repetido dos
  veces devuelve bytes y headers idénticos; sourceKind desconocido → 400;
  sourceId inexistente → 404; el SVG de origen nunca se modifica tras
  exportar).

Archivos (Frontend — `frontend/src`):
- `types/export.ts` — `ExportSourceKind` (tercer valor "dimension" sobre el
  patrón dual existente), `ExportErrorCode`.
- `api/exportApi.ts` — `getExportSvgUrl`: construye la URL del endpoint GET
  (no hay ningún otro llamado HTTP en este flujo — la descarga es una
  navegación directa, no un `fetch`).
- `components/export/ExportPanel.tsx` (+ test, 10 casos) — el "diálogo/
  resumen de exportación": selector de fuente (vector / última
  simplificación / última dimensión, las que existan), resumen de versión y
  tamaño (mm si la fuente es una dimensión, con el interno en px entre
  paréntesis; solo px si no), un botón "Revisar con Laser Checker" que
  reutiliza `useCheck` (M1-S08) para mostrar el resumen de issues bajo
  pedido — NUNCA deshabilita la descarga — y un `<a href=... download>`
  "Descargar SVG" que dispara la descarga real (no un `fetch` que ignore el
  resultado).
- `components/dimensions/DimensionPanel.tsx` (mod) — nueva prop opcional
  `onDimensionApplied` (mismo patrón que
  `SimplifyPanel.onSimplificationApplied`), para que `App.tsx` pueda
  levantar la última `DimensionVersion` aplicada y ofrecerla como fuente de
  exportación. No rompe el contrato existente (prop opcional, tests previos
  de `DimensionPanel.test.tsx` no la pasan y siguen pasando sin cambios).
- `App.tsx`/`App.css` (mod) — `buildExportSources` (extiende
  `buildCheckSources`/`buildDimensionSources` a un tercer valor), estado
  `readyDimension` levantado (invalidado en cascada al cambiar de vector o
  de simplificación, mismo criterio que `readySimplification`), nueva
  sección "Exportar SVG" tras Dimensiones físicas, pie de página actualizado
  a M1-S10.

Dependencias agregadas: ninguna (`Microsoft.Net.Http.Headers` es parte del
framework compartido ASP.NET Core, ya disponible sin paquete NuGet
adicional — se usa por primera vez en este sprint, pero no requiere
`PackageReference` nuevo).

Verificación (corrida de verdad):
- Backend: `dotnet build` → 0 errores, 0 advertencias. `dotnet test` →
  **402/402 pasaron** (352 base + 50 nuevos: 17 `ExportFileNameSanitizerTests`
  + 15 `ExportServiceTests` + 18 `ExportEndpointsTests`).
- Python: `git diff --stat -- services/python-engine` → vacío, no se tocó
  ningún archivo de `services/python-engine` (confirmado; consistente con
  que esta tarjeta no debía tocar Python en absoluto). No se corrió
  `pytest` en este entorno por no tener intérprete disponible, pero al no
  haber ningún cambio en ese árbol el resultado debería seguir en
  **211/211** exacto respecto a `main`.
- Frontend: `tsc -b && vite build` → OK. `oxlint` → sin hallazgos (exit
  code 0). `npm test` (vitest) → **121/121 pasaron** (111 base + 10 nuevos
  en `ExportPanel.test.tsx`).

## Decisiones de diseño y supuestos

- **Esquema de nombre de archivo**: `{baseSanitizado}-{sourceKind}-v{version}.svg`,
  ej. `diseño final-vector-v2.svg` o `logo-dimension-v1.svg`. `baseSanitizado`
  sale de `ProjectRecord.FileName` (el nombre original subido en M1-S02) sin
  su extensión original, con los caracteres reservados de Windows/de control
  reemplazados por `_` (para ser válido en Windows/macOS/Linux por igual,
  sin importar el SO donde corre la Web API — ver docstring de
  `ExportFileNameSanitizer`), preservando tildes/espacios/símbolos benignos.
  El sufijo `{sourceKind}-v{version}` evita colisiones si el mismo usuario
  exporta dos etapas distintas del mismo proyecto (ej. el vector y luego la
  versión simplificada) y deja explícito de qué etapa/versión salió el
  archivo, sin depender de que el usuario recuerde renombrarlo a mano.
- **Nivel de auditoría: solo logging estructurado (`ILogger`), sin registro
  persistido adicional.** `ExportService.Resolve` emite un
  `LogInformation` por cada export resuelto con éxito, con sourceKind,
  sourceId, versión, projectId/imageId y el nombre de archivo derivado (el
  timestamp lo agrega el propio pipeline de logging, que ya usa
  `AddJsonConsole` con scopes — ver `Program.cs`). Se evaluó agregar un
  sidecar JSON (mismo patrón que `PersistentDimensionVersionRegistry`) pero
  se descartó por YAGNI: nada en el spec pide poder CONSULTAR el historial
  de exports después del hecho (a diferencia de `DimensionVersion`, que sí
  necesita persistirse porque el frontend necesita poder recuperar sus
  bytes más tarde vía `GET .../dimensions/{id}`) — acá el "artefacto" ya
  está persistido por la etapa de origen, exportar no crea nada nuevo que
  necesite sobrevivir a un reinicio del proceso. Mismo criterio ya aplicado
  en Checking (M1-S08), que tampoco persiste sus análisis.
- **El Laser Checker se re-ejecuta a pedido del usuario dentro del panel de
  exportación, nunca automáticamente.** Mismo criterio de disparo manual ya
  usado en `useCheck`/`CheckPanel` (M1-S08): abrir/cambiar de fuente en el
  panel de exportación NO dispara un análisis solo; el usuario pulsa
  "Revisar con Laser Checker" explícitamente. Para una fuente `dimension`,
  el análisis se corre sobre el `sourceKind`/`sourceId` REAL de la geometría
  (el vector o la simplificación de la que se derivó esa `DimensionVersion`,
  expuestos en `DimensionResponse.sourceKind`/`sourceId`) en vez de sobre el
  propio `dimensionId`: `Checking` (M1-S08) nunca definió un sourceKind
  "dimension" porque Dimensioning (M1-S09) nunca toca los `d` de los
  `<path>` — solo reescribe `width`/`height`/`viewBox` del `<svg>` raíz — así
  que los issues de paths abiertos/duplicados son EXACTAMENTE los mismos
  que los de su fuente. Esto evita tener que extender `Checking` con un
  tercer sourceKind que no aportaría ningún análisis distinto.
- **"Diálogo" implementado como sección inline (`ExportPanel`), no como
  `<dialog>` nativo/modal.** El spec usa el término "diálogo/resumen" de
  forma laxa (mismo patrón de lenguaje que "resumen de exportación" en la
  Definition of Done). Se optó por una sección de página más, consistente
  con `CheckPanel`/`DimensionPanel`/`SimplifyPanel` (todas inline, ninguna
  es un modal), en vez de introducir el único `<dialog>` del proyecto: evita
  además el riesgo de que `HTMLDialogElement.showModal()` no esté
  completamente soportado en el entorno de tests (jsdom), y mantiene el
  mismo patrón de scroll/navegación del resto del pipeline.
- **Sin `ExportResult.UpstreamError`** (a diferencia de
  `CheckResult`/`DimensionResult`): exportar nunca llama a un motor externo
  que pueda fallar de forma controlada (timeout, servicio caído, respuesta
  inválida) — la única falla posible además de validación/404 es que el
  archivo ya persistido haya desaparecido físicamente del storage
  (`FileNotFoundException` al abrir el stream), que se maneja directamente
  en el endpoint devolviendo 404 (igual que
  `VectorizationEndpoints`/`DimensionEndpoints` ya hacen para sus propios
  GET de bytes), sin necesidad de un caso de unión adicional en
  `ExportResult`.
- **`ContentDispositionHeaderValue.SetHttpFileName`** (namespace
  `Microsoft.Net.Http.Headers`, parte del framework compartido de ASP.NET
  Core, sin paquete NuGet adicional) en vez de construir el header a mano:
  arma automáticamente el patrón de dos partes de RFC 6266 —
  `filename="..."` (con caracteres no-ASCII reemplazados por `_` como
  respaldo, para clientes viejos) + `filename*=UTF-8''...` (RFC 5987,
  percent-encoded, con el nombre real) — a partir de un único nombre
  "real" que puede tener tildes/espacios/símbolos. Verificado en los tests
  de integración leyendo `response.Content.Headers.ContentDisposition`
  (tipado) en vez de parsear el string crudo del header a mano.
- **Invalidación en cascada de `readyDimension` en el frontend**: al elegir
  un vector nuevo (`handleVectorReady`) o aplicar una simplificación nueva
  (`handleSimplificationApplied`), se limpia `readyDimension` — una
  `DimensionVersion` vieja se derivó de una fuente que ya no es "la
  vigente" en el resto del pipeline (aunque el SVG dimensionado en sí siga
  existiendo intacto y sea perfectamente exportable por su propio
  `dimensionId` si el usuario lo recordara). Mismo criterio ya usado por
  `readySimplification`/`readyVector` entre sí.

## Nota de revisión (orquestador)

Verificado independientemente (no solo confiando en el reporte): `dotnet
test` 402/402, `pytest` **212/212** (sí pude correrlo — la base correcta es
212, no 211: el PR #10, mergeado antes de arrancar esta tarjeta, agregó un
test nuevo a `test_info.py`; sin regresión, cero archivos de Python
tocados), `npm test` 121/121 (una corrida tuvo 1 fallo intermitente en
`PreprocessPanel.test.tsx`, no tocado por este sprint; una segunda corrida
completa pasó 121/121 — mismo patrón de flakiness ya documentado en M1-S08/
M1-S09). Revisé a mano `ExportFileNameSanitizer.cs`, `ExportService.cs`,
`ExportEndpoints.cs` y el mapeo `checkSourceKind`/`checkSourceId` para la
fuente "dimension" en `ExportPanel.tsx`/`App.tsx` (usa el origen real de la
geometría de `DimensionResponse.sourceKind`/`sourceId`, correcto ya que
Dimensioning nunca toca los `d`). No encontré bugs en esta ronda.

## Excepciones/limitaciones conocidas

- Heredado de tarjetas anteriores: sin verificación en `docker compose up
  --build` ni navegador real (fuera del ciclo local de build/test).
- DXF, PDF y parámetros de potencia/velocidad: fuera de alcance explícito de
  spec.md, no implementados.
- `services/python-engine` no fue tocado y `pytest` no se pudo ejecutar en
  este entorno (sin intérprete de Python disponible) -- verificado por
  ausencia total de cambios en ese árbol
  (`git diff --stat -- services/python-engine` vacío), así que no hay
  riesgo de regresión ahí.
- El nombre de archivo sugerido (`filename*`) depende de que el navegador
  del usuario lo soporte (todos los navegadores modernos lo hacen); en el
  caso límite de un cliente que solo entienda `filename=` clásico, el
  respaldo ASCII puede reemplazar tildes/caracteres no-ASCII por `_`
  (comportamiento de `SetHttpFileName`, no se reimplementó a mano) — el
  contenido del SVG nunca se ve afectado, solo el nombre sugerido de
  descarga.
- La auditoría es solo logging (ver decisión arriba): no hay forma de
  consultar "qué se exportó" desde la propia Web API después del hecho más
  allá de grepear los logs estructurados JSON.
