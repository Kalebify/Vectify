# M2-S05 · Agrupar componentes — Notas de implementación

## Resumen

Agrupación LÓGICA de componentes físicos (M2-S03): un `ComponentGroup`
referencia una lista de `componentId`s + el `ComponentSetId`/`VectorId` de
origen. Nunca toca paths, nunca crea una `VectorVersion`/`ComponentSetVersion`
nueva, nunca cambia la cantidad de piezas físicas — es una referencia lógica
pura, persistida con el mismo criterio de versionado inmutable (nunca mutar
una versión existente) que el resto del pipeline.

## Decisiones técnicas

### 1. Agrupación confinada a UN VectorId (una capa) por grupo

`ComponentGroup` referencia componentIds de la `ComponentSetVersion` de UN
solo `VectorId`. Esto está directamente respaldado por el propio criterio de
aceptación de la tarjeta: *"no debe poder crearse un grupo nuevo mezclando
IDs de dos versiones distintas"*. Como cada capa (color) tiene su propia
`ComponentSetVersion` independiente, mezclar componentes de dos capas
siempre implicaría mezclar dos `ComponentSetVersion` distintas — por lo
tanto, la única forma de cumplir esa regla es confinar cada grupo a una sola
capa/VectorId. La API refleja esto anidando los endpoints bajo
`.../vectors/{vectorId}/components/groups`: estructuralmente es imposible
enviar un request que mezcle dos VectorIds.

Interpreto la frase ambigua del cuerpo de la tarjeta *"Multi-select de
componentes (de M2-S03, dentro de una o más capas)"* como "los componentes
elegibles viven dentro del árbol de una o más capas" (el árbol tiene
múltiples capas), no como "un grupo puede combinar piezas de capas
distintas" — lo segundo violaría la regla de no-mezcla citada arriba.

### 2. Identidad de "versión de componentes que existía al crear el grupo"

`ComponentGroup.ComponentSetId` (no un número de versión) es la clave de
compatibilidad: como una `ComponentSetVersion` es inmutable una vez calculada
para un `VectorId` (M2-S03 solo cachea/reutiliza componentes ya calculados,
nunca recalcula un mismo VectorId — ver `ComponentAnalysisService`),
referenciar el `ComponentSetId` alcanza para nunca migrar automáticamente un
grupo viejo a una versión de componentes distinta. `ComponentGroupStaleness`
(clase pura, sin estado) calcula, sin lanzar excepciones, si el
`ComponentSetId` vigente cambió o si algún `componentId` referenciado ya no
aparece en la `ComponentSetVersion` actual — expuesto en cada respuesta como
`isStale`/`missingComponentIds`, recalculado en el momento de responder
(nunca cacheado), sin que el backend borre ni migre el grupo por su cuenta.

### 3. Un componente puede pertenecer a más de un grupo

Ambigüedad explícita del spec, resuelta con la recomendación documentada:
permitirlo. `ComponentGroupService.GroupAsync` no valida exclusividad entre
grupos — dos grupos pueden compartir componentIds libremente. Cubierto por
`ComponentGroupServiceTests.GroupAsync_CalledTwice_CreatesTwoCoexistingGroupsEachAdvancingTheVersion`.

### 4. Persistencia: un sidecar por VectorId, no por sesión

A diferencia de `ColorPalette` (que versiona por sesión `PaletteId`, con un
archivo por versión), los grupos de componentes siguen el patrón MÁS
CERCANO explícitamente sugerido por la tarjeta:
`PersistentComponentVersionRegistry` — un único sidecar JSON por VectorId
(`App_Data/component-groups/{ProjectId}/{ImageId}/{VectorId}.json`),
sobrescrito en cada versión nueva, con el número de versión llevado en
memoria y rehidratado al máximo visto en disco al arrancar. No hace falta un
Id de sesión separado: un VectorId tiene UN solo conjunto de grupos
evolucionando en el tiempo (agrupar/desagrupar/renombrar sobre ESE mismo
conjunto), no múltiples sesiones paralelas.

### 5. Sin llamada a Python, sin caché de resultado costoso

A diferencia de `ComponentAnalysisService` (que sí cachea un resultado
costoso de Python), `ComponentGroupService` NUNCA llama a Python ni a
storage: agrupar/desagrupar/renombrar son ediciones de metadata puras sobre
componentIds ya calculados. El único punto de contacto con M2-S03 es de
SOLO LECTURA (`IComponentAnalysisService.FindLatest`), usado exclusivamente
al crear un grupo para validar que los componentIds existan. Se mantiene sí
el mismo criterio de "nunca mutar una versión existente, cada operación
crea una versión nueva" mediante un lock (`SemaphoreSlim`) por VectorId, para
que ediciones concurrentes sobre el mismo conjunto de grupos no pisen el
avance de versión.

### 6. Mínimo de 2 componentes distintos para agrupar

No explícito en el spec, pero agrupar un único componente no tiene sentido
semántico (ya es una unidad individual). Se adoptó el mismo criterio que
`ColorPaletteService.MergeAsync` ("Seleccioná al menos 2 grupos distintos").

### 7. "Mover/seleccionar como conjunto cuando el editor lo permita"

Implementado exactamente según la interpretación pre-aprobada del spec:
clickear un grupo en el árbol (`ComponentTree`) resalta TODOS sus componentes
miembro a la vez en el canvas combinado (`LayerCanvas`), reutilizando la
selección bidireccional YA EXISTENTE de M2-S03/M2-S04 pero como un concepto
PARALELO (`highlightedGroup`) — no se reemplaza `selected` (la selección
individual de una sola pieza), ambos coexisten. No se implementó ningún
editor de arrastre nuevo (no existe tal editor en el proyecto, ver spec.md).

### 8. Rutas REST: `groupId` en la URL, no en el body

A diferencia de `ColorPaletteRenameRequest`/`ColorPaletteUnmergeRequest` (que
llevan `GroupId` en el body), rename/ungroup de grupos de componentes usan
`groupId` como parámetro de ruta:
`.../components/groups/{groupId}/ungroup` y `.../rename`. Elegido porque acá
cada operación actúa sobre UN recurso ya identificado por su propio Id (no
sobre "N ids que se combinan en uno", como el merge de paletas) — mismo
estilo que el endpoint ya existente `.../color-palette/{paletteId}/groups/{groupId}/mask`.

## Archivos creados (backend)

- `backend/Vectify.Api/Components/ComponentGroup.cs` — modelo de dominio.
- `backend/Vectify.Api/Components/ComponentGroupSetVersion.cs` — versión inmutable del conjunto de grupos de un VectorId.
- `backend/Vectify.Api/Components/ComponentGroupResult.cs` — resultado discriminado (Ready/NotFound/ValidationFailed).
- `backend/Vectify.Api/Components/IComponentGroupVersionRegistry.cs` / `InMemoryComponentGroupVersionRegistry.cs` / `PersistentComponentGroupVersionRegistry.cs` — historial versionado, mismo patrón que `*ComponentVersionRegistry`.
- `backend/Vectify.Api/Components/ComponentGroupStaleness.cs` — helper puro de validación defensiva (nunca crashea).
- `backend/Vectify.Api/Components/IComponentGroupService.cs` / `ComponentGroupService.cs` — orquestación de agrupar/desagrupar/renombrar.
- `backend/Vectify.Api/Options/ComponentGroupRegistryOptions.cs` — configuración de RootPath.
- `backend/Vectify.Api/Contracts/ComponentGroupCreateRequest.cs` / `ComponentGroupRenameRequest.cs` / `ComponentGroupResponse.cs` — contratos HTTP.
- `backend/Vectify.Api/Endpoints/ComponentGroupEndpoints.cs` — POST group/ungroup/rename, GET del conjunto vigente.

## Archivos modificados (backend)

- `backend/Vectify.Api/Program.cs` — DI + `MapComponentGroupEndpoints()`.

## Archivos de test (backend)

- `backend/Vectify.Api.Tests/Components/FakeComponentAnalysisService.cs` — doble liviano de `IComponentAnalysisService` (solo `FindLatest`).
- `backend/Vectify.Api.Tests/Components/ComponentGroupServiceTests.cs` — validación, agrupar/desagrupar/renombrar, grupos múltiples coexistiendo, hash/paths preservados exactamente, manejo sin crashear ante un `ComponentSetVersion` reemplazado.
- `backend/Vectify.Api.Tests/Components/PersistentComponentGroupVersionRegistryTests.cs` — persistencia en disco, rehidratación tras "reiniciar el proceso".

## Archivos creados (frontend)

- `frontend/src/types/componentGroups.ts` — contratos tipados (reflejan `ComponentGroupSetResponse`/`ComponentGroupPayload`).
- `frontend/src/api/componentGroupsApi.ts` — cliente HTTP (getComponentGroups/groupComponents/ungroupComponents/renameComponentGroup).
- `frontend/src/hooks/useComponentGroups.ts` — estado de multi-select (confinado a una capa), fetch automático de grupos persistidos, acciones agrupar/desagrupar/renombrar, "seleccionar como conjunto".
- `frontend/src/components/layers/ComponentGroups.test.tsx` — pruebas de multi-select confinado, agrupar/renombrar/desagrupar, resaltado de conjunto en el canvas, grupo "stale" sin romper el árbol.

## Archivos modificados (frontend)

- `frontend/src/components/layers/ComponentTree.tsx` — checkboxes de multi-select por pieza, botón "Agrupar N piezas seleccionadas", lista de grupos por capa (`ComponentGroupRow`: nombre editable, "seleccionar como conjunto", "Desagrupar", aviso si `isStale`).
- `frontend/src/components/layers/LayerCanvas.tsx` — prop `highlightedGroup` (resalta todos los componentes miembro de un grupo a la vez, independiente de `selected`).
- `frontend/src/components/layers/LayersPanel.tsx` — integra `useComponentGroups`, pasa las nuevas props a `ComponentTree`/`LayerCanvas`, muestra `groupsErrorMessage`.
- `frontend/src/App.css` — estilos `.component-tree__group*`, `.layer-canvas__component--group-selected`.

## Cobertura de criterios de aceptación

- [x] Multi-select de componentes en el árbol Layer→Components — checkboxes en `ComponentTree`, confinado a una capa (ver Decisión 1).
- [x] Acción "Agrupar" crea un `ComponentGroup` que referencia componentIds validados contra la `ComponentSetVersion` vigente; NO modifica paths, NO crea `VectorVersion`/`ComponentSetVersion` nueva, NO cambia la cantidad de piezas — `ComponentGroupService.GroupAsync`, verificado explícitamente por hash en `GroupThenUngroup_PreservesExactlyTheSameUnderlyingComponentGeometryHash`.
- [x] Acción "Desagrupar" — `ComponentGroupService.UngroupAsync`, los componentes subyacentes quedan bit-idénticos (mismo test de hash).
- [x] Renombrar grupo — `ComponentGroupService.RenameAsync` + UI inline en `ComponentGroupRow`.
- [x] "Mover/seleccionar como conjunto" — `selectGroupAsSet` + `highlightedGroup`, ver Decisión 7.
- [x] Backend .NET: modelo `ComponentGroup`, versionado inmutable, validación de existencia de componentIds contra la versión indicada, sin mezclar versiones distintas (estructuralmente imposible, ver Decisión 1).
- [x] Tests: hash/paths preservados exactamente; grupos múltiples coexistiendo (componente en 2+ grupos); manejo sin crashear cuando el `ComponentSetVersion` referenciado cambia (`ComponentGroupStaleness`, endpoints recalculan `isStale`/`missingComponentIds` en cada respuesta).
- [x] Fuera de alcance respetado: sin boolean union, sin bridges, sin reducir la cantidad de piezas físicas reales.

## Ambigüedades resueltas

1. **"Mover/seleccionar como conjunto cuando el editor lo permita"** → selección conjunta en árbol/canvas (ver Decisión 7), tal como pre-aprobado en spec.md.
2. **¿Un componente puede pertenecer a más de un grupo?** → Sí, permitido (ver Decisión 3), tal como recomendado en spec.md.
3. **Compatibilidad entre versiones de `ComponentSetVersion`** → un grupo se valida contra el `ComponentSetId` vigente al crearlo, identificado por su Id; nunca se migra automáticamente (ver Decisión 2), tal como recomendado en spec.md.
4. **"Multi-select... dentro de una o más capas"** → interpretado como "el árbol tiene múltiples capas", NO como "un grupo puede mezclar componentes de capas distintas" (ver Decisión 1) — respaldado por la regla explícita de no-mezcla de versiones.

## Verificación ejecutada

- Backend: `dotnet build` (0 errores, 0 warnings) y `dotnet test` (541/541 tests, 0 fallos) sobre TODO `Vectify.Api.Tests` (incluye los nuevos + los 526 preexistentes de M1/M2, sin regresiones).
- Frontend: `npm run build` (tsc -b + vite build, sin errores), `npm run lint` (oxlint, 0 issues), `npm test` (vitest, 165/165 tests, 21 archivos — incluye los 7 nuevos de `ComponentGroups.test.tsx`, sin regresiones).
- Python (`services/python-engine`): NO se tocó ningún archivo de este servicio (M2-S05 es puramente ASP.NET Core + React, sin ningún componente en Python). El entorno de trabajo no tiene Python instalado en el PATH, así que se corrió `pytest` dentro de un contenedor `python:3.12-slim` (Docker), instalando `requirements-dev.txt` y ejecutando `pytest -q` sobre el código YA EXISTENTE sin ninguna modificación: **325 passed, 1 warning (StarletteDeprecationWarning preexistente de fastapi/httpx, no relacionada con este sprint) en 6.75s** — confirma que no hay regresión (esperable, dado que no se tocó ningún archivo de `services/python-engine`).
