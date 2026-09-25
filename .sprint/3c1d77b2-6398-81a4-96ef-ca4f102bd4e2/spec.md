# M1-S06 · Visualizador SVG y comparación
URL: https://app.notion.com/p/3c1d77b2639881a496efca4f102bd4e2
Stack declarado: MISSING (no existe propiedad "Tecnología"/"Stack" en el board) — DERIVADO del cuerpo de la tarjeta: **React** (`VectorCanvas`, zoom/pan/reset, comparador lado a lado o slider) · **ASP.NET Core Web API** (entrega original y vector con headers/permisos correctos, sin lógica visual) · **Python sin cambios funcionales**. Continúa sobre M1-S01 a M1-S05, rama base `main` (los 5 PRs anteriores ya están mergeados).

Nota de alcance: como las tarjetas anteriores de este board, es una feature de la aplicación de vectorización, no un sitio web de marketing. Mismo acuerdo ya establecido con el usuario: se implementa directamente con `web-implementer` (build/tests locales), sin `web-design-architect` ni `web-qa-auditor`.

## Requerimiento (transcripción del cuerpo de la tarjeta)

### Contexto
Dar al usuario confianza visual antes de modificar o exportar el vector.

### Usuario podrá
Ver SVG, zoom, pan, fit-to-screen y comparar original/vector mediante lado a lado o slider.

### React
Crear `VectorCanvas` desacoplado del motor backend; render SVG seguro; controles zoom/pan/reset; overlay/comparador; responsive y manejo de diseños grandes.

### ASP.NET Core
Entregar versión vectorial y original con permisos/headers correctos. No realizar lógica visual.

### Python
Sin cambios funcionales.

### Pruebas
SVG grande/pequeño, relación de aspecto, zoom extremo, resize de ventana y comparación alineada.

### Definition of Done
Original y vector pueden inspeccionarse con la misma escala de referencia; la visualización no modifica el SVG.

### Fuera de alcance
Selección de nodos, edición, layers de color y booleanas.

## Criterios de aceptación

De la propiedad "Criterio de aceptación": "Zoom/pan y comparación original-vector funcionan sin modificar la geometría."

Ampliados por "Pruebas" y "Definition of Done" del cuerpo (declarados explícitamente, no derivados):
- [ ] `VectorCanvas` (componente React) renderiza el SVG de forma segura (reutiliza/extiende el render ya usado en `VectorizePanel` de M1-S05, que ya carga el SVG sanitizado del backend vía `<img>` — evaluar si conviene inline `<svg>` para permitir zoom/pan real con transform, documentando la decisión).
- [ ] Zoom, pan, reset y fit-to-screen funcionan y no modifican el SVG subyacente (la vectorización no cambia por interactuar con el visualizador).
- [ ] Comparación original/vector: al menos un modo (lado a lado o slider) — el spec da a elegir, el implementador decide cuál y por qué.
- [ ] Misma escala de referencia entre original y vector al comparar (Definition of Done: "pueden inspeccionarse con la misma escala de referencia").
- [ ] Responsive: funciona en distintos tamaños de ventana (resize) sin romper la alineación de la comparación.
- [ ] Maneja diseños grandes sin degradar la experiencia (no cuantificado — el implementador decide qué es "grande" y cómo lo maneja, documentado como supuesto).
- [ ] ASP.NET Core expone el original y el vector con headers/permisos correctos (probablemente ya cubierto por los endpoints existentes de M1-S02/M1-S05 — verificar si hace falta un endpoint nuevo o alcanza con los que ya existen) y explícitamente NO agrega lógica de zoom/pan/comparación del lado del servidor.
- [ ] Casos de prueba: SVG grande y pequeño, relación de aspecto distinta a 1:1, zoom extremo (mínimo y máximo), resize de ventana, comparación alineada (mismo punto de referencia visual en ambos lados).

## Referencias visuales / de marca
MISSING — no hay links ni adjuntos en la tarjeta.

## Umbrales de calidad
No aplican Lighthouse/axe por defecto del sistema en el sentido de "sitio de marketing", pero esta tarjeta SÍ es la primera puramente de interfaz visual del producto — el implementador debe prestar atención razonable a accesibilidad de los controles (zoom/pan/reset con teclado o al menos con foco visible) aunque no se audite formalmente con Lighthouse/axe. Verificación sustituta: ciclo local de `web-implementer` (build/typecheck/lint/tests) contra "Pruebas" y "Definition of Done" de arriba.

## Ambigüedades detectadas
- Propiedad "Tecnología" no existe en el board — no bloqueante, stack declarado en el cuerpo.
- "Lado a lado o slider" para la comparación: el spec deja elegir — no bloqueante, el implementador decide y documenta.
- Qué constituye "diseños grandes" y cómo manejarlos no está cuantificado — el implementador debe decidir un criterio razonable (ej. límite de nodos/paths o de dimensiones) y declararlo como supuesto.
- No está claro si hace falta un endpoint HTTP nuevo en ASP.NET Core o si los ya existentes (upload M1-S02, vectorize M1-S05) ya sirven ambos recursos — el implementador debe verificarlo primero y solo agregar lo que falte.
- Ninguna otra ambigüedad bloqueante.
