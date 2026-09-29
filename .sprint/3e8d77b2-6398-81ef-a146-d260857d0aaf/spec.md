# M2.1-S04 · Validación visual de Layers y componentes
URL: https://app.notion.com/p/3e8d77b2639881efa146d260857d0aaf

Cuarta tarjeta de MVP 2.1. Tarjeta de UX/QA, mayormente FRONTEND (Área: Frontend, Vector, Laser — sin ASP.NET Core explícito, no se espera backend nuevo salvo que haga falta algo puntual). Objetivo: demostrar visualmente, sin inspeccionar el SVG a mano, que la separación multicolor es correcta — sincronizando paleta y Layers, permitiendo aislar/comparar, y exponiendo la info ya calculada por tarjetas anteriores (fill de M2.1-S01, componentCount de M2-S03/MVP2, manufacturingOperation de M2-S07/MVP2, el endpoint consolidado de M2.1-S03).

Stack: Frontend + Vector + Laser.

## Requerimiento (transcripción del cuerpo de la tarjeta)

### Objetivo (propiedad Notion)
Demostrar visualmente que la separación multicolor es correcta permitiendo aislar colores/layers y revisar sus componentes físicos.

### Criterio de aceptación (propiedad Notion)
Click en un color/layer permite aislarlo, volver a Show All y ver paths/componentes y correspondencia con la imagen original.

### UX obligatoria
Paleta y Layers deben estar sincronizados. Click en swatch selecciona su Layer; Isolate muestra solo ese Layer; Show All restaura composición; Eye controla visibilidad sin borrar geometría.

### Información visible
Nombre, HEX, número de paths, número de componentes físicos, visibilidad y operación de fabricación si ya existe.

### Modo comparación
Permitir comparar Original / Compuesto / Layer aislado sin cambiar datos.

### Diagnóstico
Si una región está asignada al color incorrecto debe poder identificarse visualmente para que el problema pueda corregirse en etapas posteriores.

### Tests
Selección por swatch, selección por panel Layers, hide/show, isolate, show all y persistencia del estado del documento.

### Definition of Done
Con un fixture de 4–6 colores una persona puede demostrar qué geometría pertenece a cada color sin inspeccionar el SVG manualmente.

### Fuera de alcance
MISSING en el cuerpo — no hay sección "Fuera de alcance" explícita en esta tarjeta (a diferencia de las anteriores). El implementador debe inferir el alcance razonable a partir de "UX obligatoria"/"Información visible"/"Modo comparación": esto es visualización e interacción de solo lectura sobre datos ya existentes, NO edición de geometría, NO nueva lógica de backend salvo que sea trivial/de solo lectura.

## Estado actual (ya construido — punto de partida)

Ya existen y funcionan por separado: `ColorPalettePanel`/`ColorSwatchList` (paleta con swatches, M2-S01/M2.1-S02), `LayersPanel`/`LayerList` (toggle de visibilidad por capa, M2-S02), `ComponentTree` (componentes por capa con selección bidireccional lista↔canvas, M2-S03/MVP2), `ExplodedView`/`ExplodedLegend` (vista explotada, M2-S04/MVP2 — OJO: no confundir con esta tarjeta M2.1-S04, son cosas distintas de sprints distintos), `ManufacturingOperationSummary` (M2-S07/MVP2), endpoint consolidado `GET .../layers/consolidated` (fill/componentCount/manufacturingOperation combinados, M2.1-S03). Lo que NO existe hoy: sincronización cruzada palette↔layers (son paneles separados hoy, sin selección compartida), acción "Isolate" dedicada (hoy el usuario tendría que apagar manualmente cada otra capa una por una), "Show All" dedicado, y el modo comparación Original/Compuesto/Aislado.

## Criterios de aceptación ampliados

- [ ] **Sincronización paleta↔Layers**: click en un swatch de la paleta selecciona/resalta el `Layer` correspondiente en el panel Layers (mismo `groupId`), y viceversa (click en un layer del panel resalta su swatch) — selección COMPARTIDA entre ambos paneles, un solo estado de "layer seleccionado", no dos independientes.
- [ ] **Isolate**: acción de un click que muestra ÚNICAMENTE el layer seleccionado (oculta todos los demás) en el canvas combinado — reutilizar el mecanismo de visibilidad ya existente (`visibility` record en `useVectorLayers`/`LayersPanel`), no crear un sistema paralelo.
- [ ] **Show All**: acción de un click que restaura la visibilidad de TODOS los layers (deshace cualquier Isolate u ocultamiento manual previo).
- [ ] **Eye (hide/show) individual**: ya existe (M2-S02) — confirmar/reverificar que sigue funcionando junto a Isolate/Show All sin conflictos de estado (ej. ¿qué pasa si el usuario aísla un layer y después togglea el Eye de otro? comportamiento a decidir y documentar).
- [ ] **Información visible por layer**: nombre, HEX, número de paths, número de componentes físicos, visibilidad, operación de fabricación si ya existe — TODO esto ya está disponible vía el endpoint consolidado de M2.1-S03 (`GET .../layers/consolidated`), usarlo como fuente en vez de volver a combinar los datos a mano en el frontend.
- [ ] **Modo comparación**: alternar entre 3 vistas sin modificar ningún dato — "Original" (la imagen raster subida), "Compuesto" (composición de todos los layers visibles, ya existe como el canvas combinado actual), "Layer aislado" (el layer actualmente seleccionado en modo Isolate). Puramente visual/cliente, como la vista explotada de M2-S04/MVP2 (mismo espíritu: nunca persiste nada, nunca toca geometría).
- [ ] **Diagnóstico**: con Isolate + comparación contra "Original" ya alcanza para que una persona identifique visualmente si una región quedó mal asignada — no hace falta un mecanismo automático nuevo de detección de errores (eso sería IA/heurística fuera de alcance). Si el implementador quiere aprovechar las advertencias de `raster_validation` (M2.1-S03, `own_mismatch`/`contamination`) para resaltar layers con problemas conocidos, es un plus opcional, no obligatorio.
- [ ] **Tests**: selección por swatch (dispara la selección compartida), selección por panel Layers (idem, desde el otro lado), hide/show individual, isolate, show all, y "persistencia del estado del documento" durante la sesión (ej. cambiar de modo comparación y volver no debe perder la selección/visibilidad vigente — esto es persistencia de ESTADO DE UI EN MEMORIA durante la sesión del navegador, NO persistencia en el backend entre reaperturas — eso es explícitamente el alcance de M2.1-S08, "Persistencia del VectorDocument y reapertura", una tarjeta posterior).
- [ ] Fuera de alcance (inferido, ver arriba): NO editar geometría, NO nueva lógica de backend salvo lectura trivial, NO persistencia server-side de la selección/vista (eso es M2.1-S08).

## Referencias visuales / de marca
MISSING — no hay links ni adjuntos en la tarjeta.

## Umbrales de calidad
No aplican Lighthouse/axe formales, pero al ser una tarjeta explícitamente de UX/diagnóstico visual, prestar atención razonable a claridad de la interacción (nombres de acciones sin ambigüedad: "Isolate" vs "Show All" vs "Eye" deben ser inequívocos). Estándar ya usado: build sin errores/warnings nuevos (incluyendo `npm run build`, no solo `npm test` — ver gap ya documentado en M2.1-S03), los test suites completos en verde.

## Ambigüedades detectadas
- **Sección "Fuera de alcance" ausente** en el cuerpo de la tarjeta (única entre todas las de MVP2.1 hasta ahora) — se infiere el alcance razonable arriba, el implementador puede apartarse con justificación documentada.
- **Interacción Isolate + Eye simultáneos**: no especificado qué pasa si se combinan — el implementador decide un comportamiento razonable (recomendación: Isolate y el toggle manual de Eye son el MISMO mecanismo de visibilidad subyacente, así que togglear el Eye de otro layer mientras uno está "aislado" simplemente lo suma a los visibles, dejando de ser un aislamiento puro — comportamiento simple y predecible, sin estado especial "modo isolate" separado de la visibilidad normal) y lo documenta.
- Ninguna otra ambigüedad bloqueante.

## Nota de orquestación
Cuarta tarjeta de MVP 2.1, corrida en modo autónomo (mismo criterio ya establecido). Recordatorio para el propio orquestador (yo): correr `npm run build` además de `npm test` al revisar esta tarjeta, sin excepción (gap real encontrado en M2.1-S03).
