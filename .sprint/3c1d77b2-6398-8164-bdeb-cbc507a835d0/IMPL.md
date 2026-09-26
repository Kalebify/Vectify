# M2-S07 · Operación por color: corte/grabado — Notas de implementación

## Resumen

Séptima y última tarjeta de MVP2. Convierte cada `VectorLayer` (M2-S02, identificada por su
`groupId`/`VectorId` dentro de un `VectorLayerSetVersion`) en una instrucción semántica de
fabricación: `Corte`, `Grabado` o `Ignorar`. Pura metadata versionada — no toca geometría, no
llama a Python, no crea ninguna `VectorVersion`/`VectorLayerSetVersion` nueva. El precedente más
cercano en el código es `Vectify.Api.Components.ComponentGroup`/`ComponentGroupSetVersion`
(M2-S05): metadata versionada vinculada a la versión específica del recurso de origen, sin
migración automática si ese recurso se recalcula.

## Diseño del backend

### Clave de "sesión" de las asignaciones: `(PaletteId, PaletteVersion)`, no `LayerSetId`

Decisión no trivial. Se evaluaron dos claves posibles para `IManufacturingOperationVersionRegistry`:

1. `LayerSetId` (identidad inmutable de UN cálculo específico de `VectorLayerSetVersion`).
2. `(PaletteId, PaletteVersion)` — la misma combinación con la que
   `IVectorLayerSetRegistry.FindByParams` cachea un conjunto de capas ya generado
   (`VectorLayerService.GenerateLayersAsync`).

Se eligió la opción 2 porque es exactamente 1:1 con la opción 1 en la práctica (para una
`PaletteVersion` confirmada dada, `VectorLayerService` siempre reutiliza el mismo `LayerSetId` en
cache-hit, y genera uno nuevo únicamente cuando `PaletteVersion` también es nueva — ver
`VectorLayerService.GenerateLayersAsync`, rama de caché), y es literalmente lo que pide el cuerpo
de la tarjeta: *"por capa (`groupId`) Y por versión del conjunto de capas
(`VectorLayerSetVersion`/paletteId+version...)"*. `LayerSetId` igual se guarda dentro de
`ManufacturingOperationSetVersion` (solo para trazabilidad/depuración), pero NO forma parte de la
clave de búsqueda.

Consecuencia directa y deseada: si M2-S02 recalcula la paleta (`ConfirmPaletteAsync` produce una
`PaletteVersion` nueva), las asignaciones viejas simplemente no existen para la combinación
`(PaletteId, PaletteVersion)` nueva — **no hace falta ninguna lógica de migración/invalidación
explícita**, surge naturalmente de la clave (mismo criterio de "no migrar automáticamente" que ya
usa `ComponentGroup`/`ComponentSetId` en M2-S05, pero logrado con una clave compuesta en vez de un
campo `ComponentSetId` guardado por entrada). Cubierto por
`ManufacturingOperationServiceTests.FindCurrent_AfterThePaletteRegeneratesANewConfirmedVersion_DoesNotMigrateOldAssignments`
y `PersistentManufacturingOperationVersionRegistryTests.FindLatest_ForADifferentPaletteVersion_ReturnsNull`.

### Estado "sin asignar" (ambigüedad resuelta)

`ManufacturingOperationKind` (enum interno) tiene **solo 3 valores**: `Cut`, `Engrave`, `Ignore`.
Deliberadamente NO existe un cuarto valor "Unassigned" en el enum: la ausencia de una
`ManufacturingOperationAssignment` para un `groupId` dado ES el estado "sin asignar". La capa de
presentación (`Vectify.Api.Contracts.ManufacturingOperationPayload.Operation`,
`ManufacturingOperationEndpoints.ToResponse`) sintetiza el string de wire `"unassigned"` recién en
la respuesta, cruzando las capas del `VectorLayerSetVersion` vigente contra las asignaciones
existentes — nunca se persiste un registro "unassigned" explícito, y `"unassigned"` NUNCA es un
valor válido de entrada (`ManufacturingOperationParser.Parse` lo rechaza igual que cualquier otro
string desconocido). Esto respeta la recomendación explícita de spec.md: *"nunca asumir 'Corte'
por defecto silenciosamente"*.

### Validación de 3 valores exactos

`ManufacturingOperationParser` (case-insensitive, trim) traduce el wire string ↔ enum — mismo
patrón que `DimensionParameterValidator.ParseSourceKind`/`CheckSourceKind`: el contrato HTTP usa
`string`, nunca el enum serializado directamente por `System.Text.Json` (evita depender de que el
serializador emita el string correcto sin configurar un `JsonStringEnumConverter` global, que este
proyecto no usa en ningún otro módulo). Cualquier valor que no sea exactamente `"cut"`, `"engrave"`
o `"ignore"` (incluido `"unassigned"`, `null`, vacío, o cualquier palabra en español como
`"potencia"`) devuelve `400 invalid_parameters`.

### Versionado inmutable

`ManufacturingOperationService.AssignAsync`: reasignar un `groupId` reemplaza SOLO esa entrada
dentro de la lista de `Assignments` (preserva intactas las demás), y siempre llama a
`_registry.NextVersion(...)` + `_registry.Save(...)` con un `ManufacturingOperationSetVersion`
NUEVO — nunca muta el `record` anterior (que sigue siendo un objeto inmutable, `sealed record`,
en manos de quien lo recibió). Lock por `(ProjectId, ImageId, PaletteId, PaletteVersion)` vía
`SemaphoreSlim` (mismo criterio que `ComponentGroupService`/`VectorLayerService`) para que
asignaciones concurrentes sobre el mismo conjunto no pisen el avance de versión una de la otra.

### Persistencia

`PersistentManufacturingOperationVersionRegistry`: sidecar JSON por
`{ProjectId}/{ImageId}/{PaletteId}/{PaletteVersion}.json`, escritura atómica vía temp+move,
rehidratación del contador de versión al máximo persistido al arrancar — mismo patrón exacto que
`PersistentComponentGroupVersionRegistry` (M2-S05).

### Endpoints (nuevos, `Vectify.Api.Endpoints.ManufacturingOperationEndpoints`)

- `POST /api/v1/projects/{projectId}/images/{imageId}/color-palette/{paletteId}/layers/{groupId}/operation`
  — body `{ "operation": "cut" | "engrave" | "ignore" }`. `404 not_found` si esa paleta nunca
  generó capas; `404 group_not_found` si el `groupId` no existe en el `VectorLayerSetVersion`
  vigente; `400 invalid_parameters` si el valor no es uno de los 3 aceptados; `200 OK` con el
  conjunto completo (leyenda + resumen) en éxito.
- `GET /api/v1/projects/{projectId}/images/{imageId}/color-palette/{paletteId}/layers/operations`
  — recupera, sin mutar nada, el conjunto completo (todas las capas, incluidas las
  `"unassigned"`) + resumen agregado. `404 not_found` si esa paleta nunca generó capas.

Anidados bajo `.../color-palette/{paletteId}/layers`, un nivel más adentro que el propio conjunto
de capas de M2-S02 — mismo criterio de anidamiento que `ComponentGroupEndpoints` bajo
`.../vectors/{vectorId}`.

### Export metadata futura (ambigüedad resuelta, explícita en spec.md)

El cuerpo de la tarjeta dice *"incluirlos en export metadata FUTURA"* y spec.md aclara
explícitamente en "Ambigüedades detectadas" que esto NO es requisito de esta tarjeta. **No se
tocó** `Vectify.Api.Export.ExportEndpoints`/`ExportService` ni el SVG exportado: el dato de
operación por capa existe, está persistido y se puede leer (`GET .../layers/operations`), pero no
se inyecta en el SVG real de M1-S10. Se documenta acá para que quede explícito que es una omisión
deliberada, no un olvido.

## Diseño del frontend

Extiende el panel Layers YA EXISTENTE (M2-S02/M2-S04/M2-S05/M2-S06) — **no** se creó un panel
paralelo, siguiendo la instrucción explícita de la tarjeta:

- `LayerList.tsx` (modificado): cada fila de capa agrega (a) una leyenda visual (`<span
  className="manufacturing-operation-badge--{cut|engrave|ignore|unassigned}">`, con color
  distintivo — verde/`--ok` para Corte, acento para Grabado, neutro para Ignorar, ámbar/`--warn`
  para "sin asignar", para que llame la atención antes de exportar) y (b) un `<select>` nativo
  (Corte/Grabado/Ignorar) para asignar/reasignar. `operationFilter` oculta (solo de esta lista,
  sin tocar visibilidad/canvas/exploded view) las filas que no matchean el filtro vigente.
- `manufacturingOperationLabels.ts` (nuevo, módulo de solo-constantes): copy compartido
  (`OPERATION_LABEL`) entre `LayerList` y `ManufacturingOperationSummary` — en su propio archivo
  en vez de un named export adicional de un componente, para no disparar el warning de oxlint
  `react(only-export-components)` (rompe Fast Refresh).
- `ManufacturingOperationSummary.tsx` (nuevo): filtro por operación (`radiogroup`, "Todas /
  Corte / Grabado / Ignorar / Sin asignar") + el resumen textual "N en Corte, M en Grabado, K
  ignoradas, J sin asignar (de T capas en total)". Se renderiza dentro del panel Layers (no cerca
  del `ExportPanel` real) — ver "Ambigüedad resuelta: ubicación del resumen" más abajo.
- `useManufacturingOperations.ts` (nuevo hook): recupera automáticamente (una sola vez por
  `layerSetId`, sin que el usuario tenga que pedirlo — mismo criterio que `useComponentGroups`)
  las asignaciones ya persistidas apenas el conjunto de capas actual está disponible; `assign()`
  dispara el POST y actualiza el estado local con la respuesta completa del servidor (fuente de
  verdad única, sin estado optimista). Si la Web API no está disponible al recuperar, NO reintenta
  automáticamente (evita una clase entera de comportamiento no determinista/reintentos ocultos
  fuera de una acción explícita del usuario) — simplemente el panel muestra el estado local por
  default (todo "unassigned") hasta que el usuario asigne algo o refresque.
- `manufacturingOperationsApi.ts` / `types/manufacturingOperations.ts` (nuevos): mismo patrón que
  `componentGroupsApi.ts`/`types/componentGroups.ts`.
- `LayersPanel.tsx` (modificado): wiring del hook + los dos componentes nuevos en el sidebar,
  cerca del `LayerList`.
- `App.css` (modificado): tokens de color reutilizados de los ya existentes (`--ok`/`--ok-bg`,
  `--accent`, `--warn`/`--warn-bg`, `--border`, `--text`) — ningún color nuevo inventado.

### Ambigüedad resuelta: ubicación del resumen "antes de exportar"

spec.md pide el resumen "visible antes de exportar" y sugiere integrarlo "cerca o dentro del
panel de exportación (M1-S10)". En este proyecto, `ExportPanel`/`buildExportSources` (App.tsx)
operan exclusivamente sobre el pipeline de UN SOLO vector de MVP1 (vector → simplificación →
dimensión), completamente ajeno a `paletteId`/capas de color de MVP2 — no reciben ni `paletteId`
ni ninguna noción de "capa". Acoplar el resumen de M2-S07 a `ExportPanel` habría requerido
modificar su contrato/props (agregar `paletteId` opcional) para una tarjeta que la propia spec.md
dice explícitamente que NO necesita tocar el export real. Se optó por renderizar el resumen dentro
del panel Layers (la sección inmediatamente anterior en la página, dentro del mismo flujo de
"antes de convertir a fabricación"), sin modificar `ExportPanel` ni `ExportEndpoints` — respeta al
pie de la letra "sin necesariamente modificar el export en sí".

## Pruebas

### Backend (`backend/Vectify.Api.Tests/ManufacturingOperations/`)

- `ManufacturingOperationServiceTests.cs` (17 tests): validación (paleta sin capas, groupId
  inexistente, los 3 valores exactos con case-insensitive/trim), **persistencia** (asignar y
  `FindCurrent` devuelve el mismo valor), **cambios** (reasignar crea versión nueva sin mutar el
  `record` anterior — el objeto viejo en memoria sigue reportando el valor viejo, verificando
  literalmente "la anterior queda en el historial"; reasignar una capa preserva intactas las
  demás), **capas ignoradas**, **combinación corte+grabado** en el mismo conjunto, y sin
  migración automática tras un recálculo de paleta.
- `PersistentManufacturingOperationVersionRegistryTests.cs` (6 tests): guardar/leer, sidecar en
  disco, rehidratación de versión tras "reiniciar el proceso", y que una `PaletteVersion`
  distinta no encuentra las asignaciones de otra.
- `FakeVectorLayerService.cs`: doble de prueba de `IVectorLayerService` (mismo criterio que
  `FakeComponentAnalysisService` de M2-S05).

### Frontend (`frontend/src/components/layers/ManufacturingOperations.test.tsx`)

6 tests, mismo estilo de fixtures que `ComponentGroups.test.tsx` (fetch fake que dispatcha por
URL+método): estado inicial "todas sin asignar", asignar Corte actualiza leyenda+resumen,
combinación Corte+Grabado, marcar Ignorar, y el filtro "mostrar solo Corte" oculta filas de la
lista sin afectar el canvas combinado.

## Archivos creados

### Backend (`backend/Vectify.Api`)
- `ManufacturingOperations/ManufacturingOperationKind.cs` — enum de los 3 valores.
- `ManufacturingOperations/ManufacturingOperationParser.cs` — wire string ↔ enum.
- `ManufacturingOperations/ManufacturingOperationAssignment.cs` — asignación por capa.
- `ManufacturingOperations/ManufacturingOperationSetVersion.cs` — conjunto versionado.
- `ManufacturingOperations/ManufacturingOperationResult.cs` — resultado de `AssignAsync`.
- `ManufacturingOperations/IManufacturingOperationVersionRegistry.cs` / `InMemory...cs` /
  `PersistentManufacturingOperationVersionRegistry.cs` — historial persistido.
- `ManufacturingOperations/IManufacturingOperationService.cs` / `ManufacturingOperationService.cs`
  — orquestación.
- `Contracts/ManufacturingOperationRequest.cs` / `ManufacturingOperationResponse.cs`.
- `Options/ManufacturingOperationRegistryOptions.cs`.
- `Endpoints/ManufacturingOperationEndpoints.cs`.

### Backend tests
- `Vectify.Api.Tests/ManufacturingOperations/FakeVectorLayerService.cs`
- `Vectify.Api.Tests/ManufacturingOperations/ManufacturingOperationServiceTests.cs`
- `Vectify.Api.Tests/ManufacturingOperations/PersistentManufacturingOperationVersionRegistryTests.cs`

### Frontend
- `frontend/src/types/manufacturingOperations.ts`
- `frontend/src/api/manufacturingOperationsApi.ts`
- `frontend/src/hooks/useManufacturingOperations.ts`
- `frontend/src/components/layers/manufacturingOperationLabels.ts`
- `frontend/src/components/layers/ManufacturingOperationSummary.tsx`
- `frontend/src/components/layers/ManufacturingOperations.test.tsx`

## Archivos modificados

- `backend/Vectify.Api/Program.cs` — registro de `ManufacturingOperationRegistryOptions`,
  `IManufacturingOperationVersionRegistry`, `IManufacturingOperationService`,
  `MapManufacturingOperationEndpoints()`.
- `frontend/src/components/layers/LayerList.tsx` — selector + leyenda + filtro por fila.
- `frontend/src/components/layers/LayersPanel.tsx` — wiring de `useManufacturingOperations` +
  `ManufacturingOperationSummary`.
- `frontend/src/App.css` — estilos nuevos (`.layer-row__operation`, `.manufacturing-operation-badge*`,
  `.manufacturing-operation-summary*`, `.layer-list__empty`).

## Fuera de alcance (respetado)

No se implementó potencia, velocidad ni configuración de máquina/láser — ni en esta tarjeta ni en
ninguna otra de MVP2 (confirmado: no hay ningún campo de ese tipo en `ManufacturingOperation*`).

## Cobertura de criterios de aceptación

| Criterio | Cobertura |
|---|---|
| 3 valores exactos + estado "sin asignar" propio, sin default silencioso | `ManufacturingOperationKind` (3 valores) + wire `"unassigned"` sintetizado solo en respuestas; tests de validación |
| No modifica geometría / no crea VectorVersion o VectorLayerSetVersion nueva | `ManufacturingOperationService` nunca llama a `IVectorVersionRegistry`/`IPython*Client`; solo lee `IVectorLayerService.FindLatest` |
| Vinculado a la versión específica del conjunto de capas, sin migración automática | Clave `(PaletteId, PaletteVersion)`; test `FindCurrent_AfterThePaletteRegeneratesANewConfirmedVersion_DoesNotMigrateOldAssignments` |
| Validación de los 3 valores | `ManufacturingOperationParser.Parse` + tests `[Theory]` |
| Persistencia versionada, nunca muta una existente | `NextVersion`+`Save` siempre crean un `record` nuevo; test de reasignación |
| Selector por capa en el panel Layers existente | `LayerList.tsx` (modificado, no un panel paralelo) |
| Filtros por operación | `ManufacturingOperationSummary` (radiogroup) + `LayerList` filtra sus filas |
| Resumen antes de exportar | `ManufacturingOperationSummary` (texto agregado) dentro del panel Layers |
| Leyenda visual clara | Badge con color distintivo por operación en cada fila |
| Tests: persistencia, cambios, ignoradas, combinación corte+grabado | `ManufacturingOperationServiceTests` (backend) + `ManufacturingOperations.test.tsx` (frontend) |
| Fuera de alcance: potencia/velocidad/máquina | No implementado, ver arriba |

## Nota sobre verificación (entorno de esta sesión)

En este entorno de ejecución, correr `npx vitest run` con la concurrencia por default (varios
workers en paralelo) produjo timeouts intermitentes de arranque de workers (`[vitest-pool]:
Failed to start forks worker` / `Timeout waiting for worker to respond`) y, bajo esa misma carga,
una falla puntual y no determinista en `ExplodedView.test.tsx` (archivo preexistente, no tocado
por esta tarjeta) por una comparación de conteo de llamadas a `fetch` sensible a timing. Se
confirmó que esta flakiness es **preexistente y ambiental** (reproducible corriendo el código
ORIGINAL sin ningún cambio de esta tarjeta, vía `git stash`) y no un defecto introducido por
M2-S07: limitando la concurrencia (`--maxWorkers=2`) el suite completo pasa de forma consistente
y repetible (175/175, verificado en 3 corridas consecutivas). Los números reportados en el reporte
final usan esa corrida estable.
