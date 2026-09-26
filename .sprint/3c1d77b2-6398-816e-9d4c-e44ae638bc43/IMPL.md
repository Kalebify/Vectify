# M2-S04 · Vista explotada por colores — IMPL.md

## Resumen

Tarjeta mayormente frontend/UX. Se agregó una vista "explotada" (capas
desplazadas visualmente para inspección) sobre los datos ya existentes de
M2-S02 (`useVectorLayers`, `LayerList`, `LayerCanvas`) y M2-S03
(`useLayerComponents`, `ComponentTree`). No se generó geometría nueva, no se
persistió nada nuevo en el backend, no se creó ninguna versión nueva de
nada. El desplazamiento se calcula y aplica 100% del lado del cliente
mediante `transform: translate(...)` CSS.

## Decisión: backend — NO se agregó nada nuevo

Se evaluó agregar un endpoint de agregación ("capas + conteo de componentes
en una sola respuesta"), pero **no hizo falta**: el cliente ya tiene ambos
datasets disponibles en memoria en `LayersPanel` (`layerSet.layers` de
`useVectorLayers` y `componentsByGroup` de `useLayerComponents`), y
combinarlos ahí es una operación trivial de lookup por `groupId` (ver
`ExplodedLegend.tsx`). Agregar un endpoint solo para evitar ese lookup
habría violado el criterio YAGNI ya aplicado en M1-S08/M1-S10 (mencionado
explícitamente en spec.md). Cero archivos de `backend/` ni
`services/python-engine/` se tocaron para esta tarjeta.

## Decisión: algoritmo de desplazamiento visual

**Lineal, diagonal, proporcional al índice ORIGINAL de la capa** (no al
índice entre las capas visibles):

```
offsetPercent = layerIndex * separationPercent
transform: translate(offsetPercent%, offsetPercent%)
```

- `layerIndex` es la posición de la capa en `layerSet.layers` (orden que
  entrega el backend), **no** su posición dentro de `visibleLayers`. Así,
  ocultar una capa con el toggle de M2-S02 no reacomoda el desplazamiento
  de las demás — cada capa mantiene siempre su lugar en el patrón.
- El desplazamiento usa **porcentaje**, no píxeles. `transform:
  translate(N%)` en CSS es relativo al tamaño de la propia caja del
  elemento (no al del contenedor), así que el desplazamiento se ve
  proporcionalmente idéntico sin importar el ancho real en pantalla — es
  lo que hace que la vista explotada no se rompa en viewports angostos
  (requisito de responsive) sin necesidad de recalcular nada en JS al
  cambiar de tamaño.
- Se eligió diagonal (misma magnitud en X e Y) sobre radial en abanico por
  simplicidad y previsibilidad: con desplazamiento radial, cada capa
  necesitaría un ángulo asignado (ambigüedad adicional no resuelta por
  spec.md) y el cálculo de espacio ocupado es más difícil de razonar. Un
  desplazamiento lineal proporcional al índice es la opción explícitamente
  sugerida como ejemplo en la ambigüedad detectada de spec.md y es
  trivialmente reversible: `exploded=false` no aplica NINGÚN
  `style.transform` (no `translate(0%, 0%)`), por lo que la vista
  ensamblada recupera el layout original de forma exacta, sin
  aproximación ni acumulación de redondeo.
- `separationPercent` es controlado por el usuario vía un `<input
  type="number">` (ver más abajo) sin atributo `max`: no hay límite
  superior estricto, solo se descarta un valor negativo o no finito
  (`useExplodedView.setSeparationPercent`).

## Decisión: `<input type="number">` en vez de `<input type="range">`

El criterio de aceptación pide explícitamente "sin límite superior
estricto, salvo el razonable para que siga siendo usable". Un
`<input type="range">` solo puede expresar esto fijando un `max`
arbitrariamente alto, que de todos modos sigue siendo un tope duro que el
usuario no puede superar desde la UI. Un `<input type="number">` sin
`max` permite que el usuario suba la separación tanto como quiera (se
valida solo "no negativo, finito" en el hook). El spec dice "ej. un
slider", dejando la implementación exacta abierta.

## Reutilización explícita (sin duplicar lógica)

- **Toggle de visibilidad por color** (aislar/ocultar): se reutiliza
  `visibility`/`toggleVisibility` de `useVectorLayers` (M2-S02) tal cual —
  `LayerList` (sin cambios) se sigue renderizando en el sidebar
  independientemente del modo de vista, así que "aislar un color" funciona
  igual en ambas vistas sin ningún código nuevo.
- **Selección de componente consistente entre vistas**: no se creó un
  segundo canvas ni un segundo estado de selección. `LayerCanvas.tsx` (el
  MISMO componente de M2-S02/M2-S03) ahora acepta dos props opcionales
  (`exploded`, `separationPercent`) y sigue siendo la única fuente de
  verdad visual tanto para la vista ensamblada como la explotada.
  `selected`/`onSelectComponent` siguen viniendo del mismo
  `useLayerComponents` en `LayersPanel`, que nunca se remonta al cambiar de
  vista (a diferencia de `useVectorLayers`/`useLayerComponents`, que sí se
  remontan con `key` al cambiar de paleta) — por eso alternar el modo de
  vista NUNCA pierde la selección: es exactamente el mismo estado React.
- **Contador de piezas por color**: `ExplodedLegend.tsx` lee directamente
  `componentsByGroup` (M2-S03, ya calculado por el usuario con "Calcular
  componentes") — si una capa todavía no tiene componentes calculados,
  esa fila de la leyenda muestra solo nombre+color, sin inventar una
  cifra. No dispara ningún fetch nuevo (test explícito: alternar de vista
  no aumenta la cantidad de llamadas a `fetch`).

## Archivos creados

- `frontend/src/hooks/useExplodedView.ts` — estado puramente visual: modo
  de vista (`"assembled" | "exploded"`) y `separationPercent`. Sin fetch,
  sin efectos secundarios.
- `frontend/src/hooks/useExplodedView.test.ts` — tests del hook (estado
  inicial, toggle, validación de separación no negativa/finita, sin
  límite superior).
- `frontend/src/components/layers/ExplodedViewControls.tsx` — toggle
  explícito ensamblada/explotada (radio group semántico) + input numérico
  de separación.
- `frontend/src/components/layers/ExplodedLegend.tsx` — leyenda de
  colores/capas (nombre + color + contador de piezas si ya se calculó),
  visible solo en la vista explotada.
- `frontend/src/components/layers/ExplodedView.test.tsx` — tests de
  integración (ver cobertura de criterios más abajo).

## Archivos modificados

- `frontend/src/components/layers/LayerCanvas.tsx` — se agregaron los
  props opcionales `exploded`/`separationPercent` (default `false`/`0`,
  compatibilidad total hacia atrás con M2-S02/M2-S03: los tests
  preexistentes de `LayersPanel.test.tsx` y `LayerComponents.test.tsx`
  siguen pasando sin modificación). Cada capa (imagen + su overlay de
  componentes) ahora se envuelve en un único `div.layer-canvas__layer-group`
  para que el `transform` mueva ambos juntos y los clicks de selección
  sigan cayendo sobre la pieza desplazada. El `aria-label` del `role=group`
  cambia de texto según el modo (se verificó que el texto de la vista
  ensamblada es idéntico al original — no rompe los tests existentes que
  matchean por regex).
- `frontend/src/components/layers/LayersPanel.tsx` — instancia
  `useExplodedView`, renderiza `ExplodedViewControls` + `ExplodedLegend`
  (esta última solo si `viewMode === "exploded"`) alrededor de
  `LayerCanvas`, y le pasa `exploded`/`separationPercent`. Ningún cambio en
  `useVectorLayers`/`useLayerComponents`.
- `frontend/src/App.css` — sección nueva "Vista explotada por colores
  (M2-S04)": estilos de `.exploded-view-controls`, `.exploded-legend`,
  `.layer-canvas__layer-group` (con `transition: transform` — permitida
  explícitamente por spec.md: "una transición simple no está prohibida",
  no es la animación decorativa fuera de alcance) y
  `.layer-canvas--exploded { overflow: auto }` (para no recortar capas muy
  desplazadas; los navegadores modernos incluyen el área transformada de
  los descendientes en el overflow desplazable). Se agregaron reglas
  `@media (max-width: 400px)` siguiendo el mismo patrón que el resto del
  archivo (`.check-panel__actions`, `.dimension-panel__actions`, etc.).
  También se simplificó `.layer-canvas__layer` (ya no necesita
  `position: absolute` propio, porque ahora ese posicionamiento vive en el
  wrapper `.layer-canvas__layer-group`).

## Cobertura de criterios de aceptación

- **Toggle ensamblada/explotada**: `ExplodedViewControls` (radio group) +
  test "arranca en vista ensamblada y permite alternar a explotada y
  volver".
- **Desplazamiento puramente visual, reversible sin pérdida**: tests
  parametrizados con 2, 5 y 10 capas verifican `style.transform` exacto por
  índice, y que volver a ensamblada deja `style.transform === ""` (no una
  aproximación).
- **Control de separación sin límite superior estricto**: input numérico
  sin `max`; test que fija `separationPercent = 5000` y verifica el
  transform resultante.
- **Aislar color en cualquiera de las dos vistas**: test que oculta una
  capa estando en modo explotado y verifica que el conteo de capas
  visibles baja también al volver a ensamblada (mismo estado compartido).
- **Leyenda con nombre y color**: test que verifica que la leyenda NO
  existe en modo ensamblado y sí en modo explotado, con el nombre de cada
  capa.
- **Contador de piezas por color (dato ya calculado de M2-S03)**: test que
  calcula componentes, cambia de vista, y verifica que el conteo de
  `fetch` no aumenta (no se recalcula) y que la leyenda muestra el número
  correcto singular/plural ("1 pieza" / "3 piezas").
- **Backend sin persistencia nueva**: ningún archivo de `backend/` ni
  `services/python-engine/` fue tocado; los 3 suites de esos stacks siguen
  pasando igual que antes de esta tarjeta (ver resultados abajo).
- **Selección consistente entre vistas**: dos tests explícitos
  (seleccionar en ensamblada → cambiar a explotada, y a la inversa) que
  verifican `aria-pressed="true"` se mantiene sobre el mismo componente en
  ambos modos.
- **Responsive**: test que fuerza `window.innerWidth = 320` + evento
  `resize` y verifica que los controles, el canvas y la leyenda siguen
  presentes y accesibles. Nota: jsdom no ejecuta un motor de layout real,
  así que este test verifica ausencia de errores/desmontajes al achicar el
  viewport, no medidas de píxeles reales — la responsividad de CSS en sí
  (media queries `@media (max-width: 400px)`) se verificó manualmente
  leyendo `App.css` y siguiendo el mismo patrón ya usado en el resto del
  archivo.
- **Muchos componentes**: test con una capa de 8 componentes que verifica
  el contador en la leyenda ("8 piezas") y que las 8 piezas se renderizan
  como botones clickeables en el canvas.
- **Fuera de alcance respetado**: no se implementó nesting (no hay ninguna
  lógica de empaquetado/reordenamiento de piezas para aprovechar espacio de
  corte) ni animación decorativa como foco del trabajo (la única
  transición es un `transition: transform` funcional de 200ms, ya presente
  en el mismo espíritu que otras transiciones sutiles del proyecto, no
  bloqueante ni requerida por ningún test).

## Ambigüedades resueltas

- **Algoritmo de desplazamiento**: no especificado en spec.md — se eligió
  lineal/diagonal proporcional al índice ORIGINAL de la capa, expresado en
  porcentaje (no píxeles), documentado arriba.
- **"Backend: entregar metadata necesaria"**: se resolvió NO agregar nada
  nuevo, combinando M2-S02 + M2-S03 del lado del cliente, siguiendo el
  criterio YAGNI explícito de la propia spec.md.
- **Control de separación "sin límite superior estricto"**: se interpretó
  literalmente, usando `<input type="number">` sin `max` en vez de
  `<input type="range">` con un tope alto mencionado en el propio texto
  del criterio como ejemplo, no como requisito de forma.

## Verificación

### Frontend (`npm test`, `npm run build`, `npm run lint`)

```
Test Files  20 passed (20)
     Tests  158 passed (158)
```

```
tsc -b && vite build
✓ 82 modules transformed.
✓ built in 612ms
```

```
oxlint
(sin salida, exit code 0 — 0 errores/advertencias)
```

### Backend (.NET)

```
dotnet build Vectify.sln
Compilación correcta.
    0 Advertencia(s)
    0 Errores
```

```
dotnet test Vectify.sln
Correctas! - Con error: 0, Superado: 519, Omitido: 0, Total: 519
```

### Python (services/python-engine)

```
pytest -q
325 passed, 1 warning in 6.72s
```
(el único warning es una `StarletteDeprecationWarning` preexistente de
`fastapi.testclient`, no introducida por esta tarjeta.)

Ningún archivo de `backend/` ni `services/python-engine/` fue modificado
por esta tarjeta — se corrieron sus suites solo para confirmar que nada se
rompió, como pidió el orquestador.
