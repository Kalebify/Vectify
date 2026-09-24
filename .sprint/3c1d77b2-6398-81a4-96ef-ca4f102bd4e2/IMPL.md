status: ok

Archivos:
- `frontend/src/hooks/useCanvasTransform.ts` — hook con el estado puro de zoom/pan (`scale`, `panX`, `panY`): `zoomBy` (con anchor opcional para zoom-al-cursor), `panBy`, `reset` (1:1) y `fitToScreen` (contain). Nunca toca el SVG/imagen, solo produce valores para un `transform` CSS.
- `frontend/src/components/vectorize/VectorCanvas.tsx` — panel individual interactivo: wheel-zoom (listener nativo `{passive:false}`), pan por arrastre vía Pointer Events + captura de puntero (con fallback defensivo), throttle de pan vía `requestAnimationFrame`, controles de teclado (flechas/±) y `aria-label` descriptivo; `ResizeObserver` y `rAF` se limpian al desmontar.
- `frontend/src/components/vectorize/VectorComparison.tsx` — comparador lado a lado: una única instancia de `useCanvasTransform` compartida por los dos `VectorCanvas` (original y vector), barra de herramientas (Alejar/Acercar/Restablecer/Ajustar a pantalla) con botones deshabilitados en los límites de escala, ajuste automático a pantalla una sola vez al montar, y nota visible cuando el diseño es "grande".
- `frontend/src/components/vectorize/VectorizePanel.tsx` (mod) — integra `VectorComparison` (se monta con `key={vector.vectorId}` para resetear zoom/pan en cada vectorización nueva); define el criterio de "diseño grande" y usa el original real (M1-S02) en vez de la máscara intermedia como término de comparación.
- `frontend/src/App.tsx`, `App.css` (mod) — pasa la URL/dimensiones del original real a `VectorizePanel`; estilos nuevos para `.vector-comparison`/`.vector-canvas`.
- `frontend/src/test/setup.ts` (mod) — stub mínimo de `ResizeObserver` para jsdom.
- Tests nuevos: `useCanvasTransform.test.ts`, `VectorCanvas.test.tsx`, `VectorComparison.test.tsx` (+ `VectorizePanel.test.tsx` actualizado) — zoom/pan/reset/fit-to-screen, anchor de zoom, límites de escala, aspect ratio ≠1:1, flujo completo.

Backend (ASP.NET Core): **sin cambios** — se verificó que `GET .../vectors/{vectorId}` (M1-S05) y `GET .../original` (M1-S02) ya sirven los bytes con `Content-Type` correcto y CORS ya expone los headers necesarios. No hizo falta ningún endpoint nuevo, y no se agregó lógica visual del lado del servidor (cumple el spec: "No realizar lógica visual").
Python: sin cambios, no se tocó `services/python-engine`.

Dependencias agregadas: ninguna.

Verificación (corrida de verdad):
- tests: `npx vitest run` → **61/61 pasaron** (9 archivos).
- typecheck: `npx tsc -b` → OK.
- build: `npm run build` → OK (dist generado).
- lint: `npx oxlint` → OK, sin hallazgos.

Decisiones de diseño y supuestos:
- **`<img>` + transform CSS, no `<svg>` inline**: mismo criterio de defensa en profundidad que M1-S05 (el navegador nunca ejecuta script embebido en un `<img>`, aunque el SVG ya esté saneado en el backend). Trade-off documentado: en zoom extremo puede verse levemente rasterizado en vez de vectorialmente nítido — decisión deliberada por fluidez con diseños grandes.
- **Comparación lado a lado (no slider)**: con una única transformación compartida entre dos paneles, es trivial garantizar "misma escala de referencia" sin cálculos de alineación de slider.
- **"Original" = imagen subida (M1-S02)**, no la máscara B/N intermedia (que ya tiene su propia comparación en `ThresholdPanel`, M1-S04).
- **Criterio de "diseño grande"**: `pathCount > 500` OR `approxNodeCount > 5000` OR área > 4,000,000 px². Cuando se cumple, se reduce el zoom máximo de 8× a 4× y se muestra una nota visible. Umbral heurístico, no medido contra dataset real.
- **Reset (1:1) vs Fit to screen (contain)**: controles distintos e intencionalmente no equivalentes.
- **Sin pinch-zoom táctil dedicado**: cubre rueda/trackpad + botones/teclado; pan con un dedo funciona (Pointer Events), pinch de dos dedos no — fuera de alcance de un ticket sin diseño visual explícito.
- **Límites de escala**: `minScale=0.1`, `maxScale=8` (4 si el diseño es "grande").

## Bugs reales encontrados y corregidos por el implementador antes de reportar (revisión propia)
- `setPointerCapture`/`hasPointerCapture`/`releasePointerCapture` podían faltar o lanzar (jsdom, navegadores viejos, pointerId inválido) sin guardas — el handler de `pointerdown` explotaba. Arreglado con `?.()` + `try/catch`, degradando a "drag sin captura" en vez de romper la interacción.
- El listener de `wheel` vía `onWheel` de JSX es pasivo desde React 17+ — `preventDefault()` no lo detiene de forma confiable, así que la rueda scrolleaba la página entera en vez de hacer zoom. Arreglado adjuntando el listener a mano con `{passive:false}`.
- `ResizeObserver` y el `requestAnimationFrame` pendiente de pan se cancelan explícitamente en el cleanup al desmontar, evitando actualizar estado de un componente ya desmontado.
- El ajuste automático a pantalla corre una sola vez por vector nuevo (no en cada resize posterior), para no pisar el zoom/pan elegido por el usuario.

**Excepción heredada**: `docker compose up --build` y navegador real siguen sin verificar (mismo motivo que las tarjetas anteriores).
