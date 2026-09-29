# M2.1-S05 · Spike técnico del motor del Editor General
URL: https://app.notion.com/p/3e8d77b2639881adbfc9df4496ec8b81

Quinta tarjeta de MVP 2.1. A diferencia de TODAS las tarjetas anteriores de este proyecto, esta NO es una feature de producto — es un SPIKE técnico: investigación ejecutable + documento de decisión (ADR). El entregable es evidencia comparativa real (demos funcionando) más una recomendación justificada, NO el editor completo (eso es M2.1-S06, la tarjeta siguiente, que depende explícitamente de esta).

Stack: Frontend + Vector.

## Requerimiento (transcripción del cuerpo de la tarjeta)

### Objetivo (propiedad Notion)
Elegir mediante una prueba ejecutable el motor gráfico del editor, validando SVG, selección, layers, zoom/pan, transformaciones y futura edición de nodos.

### Criterio de aceptación (propiedad Notion)
Existe una demo y ADR que compara candidatos con los mismos casos y selecciona una opción justificadamente antes de construir el Workspace.

### Por qué existe
El editor será el núcleo del producto. No elegir librería solo por popularidad.

### Candidatos
Evaluar **Paper.js**, **Fabric.js** y **Konva/react-konva**; se permite otro candidato si el agente justifica por qué.

### Spike obligatorio
Con el MISMO SVG multicolor:
1. Importar SVG.
2. Mantener layers/IDs o mapearlos sin pérdida.
3. Zoom/Pan.
4. Single y multi-select.
5. Move/scale/rotate.
6. Hide/show/isolate Layer.
7. Hit-test de path.
8. Demostrar viabilidad de selección de segmentos/handles para futuro Bézier.
9. Exportar/serializar y comprobar round-trip.
10. Medir rendimiento con un SVG de complejidad media.

### Criterios
Fidelidad SVG; facilidad React; path/node editing; hit-testing; layers; transforms; serialization; performance; mantenibilidad; TypeScript; extensibilidad.

### Arquitectura
React controla layout, toolbar, inspector, layers y estado de aplicación. El motor gráfico controla Canvas y geometría interactiva. **No convertir el motor gráfico en source of truth del dominio sin una decisión explícita.**

### Entregable
ADR con matriz de decisión, demo y recomendación. **No construir todavía el editor completo.**

### Definition of Done
S06 puede comenzar sin volver a debatir la librería y existe evidencia ejecutable de las capacidades críticas.

## Criterios de aceptación ampliados

- [ ] **Spike ejecutable real para los 3 candidatos** (Paper.js, Fabric.js, Konva/react-konva) — no un análisis solo de documentación/artículos: código funcionando, con el MISMO SVG multicolor de entrada para los 3 (reutilizar un SVG real generado por el pipeline ya existente — ej. el fixture de 4 colores de la evidencia de M2.1-S01/M2.1-S02/M2.1-S03, o un `VectorLayerSetVersion` real generado end-to-end contra el stack corriendo — NO un SVG inventado a mano desconectado del pipeline real).
- [ ] **Los 10 puntos del "Spike obligatorio"** cubiertos para cada candidato, con evidencia concreta de qué funcionó, qué no, y con qué esfuerzo/código:
  1. Importar SVG.
  2. Mantener layers/IDs (del modelo `VectorLayer` ya definido en M2.1-S03: `groupId`, `fill`, etc.) o mapearlos sin pérdida.
  3. Zoom/Pan.
  4. Single y multi-select.
  5. Move/scale/rotate.
  6. Hide/show/isolate Layer (mismo concepto ya construido en React puro en M2.1-S04 — el spike debe mostrar que el motor gráfico PUEDE soportarlo a nivel de canvas, no reimplementar la UX de M2.1-S04).
  7. Hit-test de path.
  8. Viabilidad de selección de segmentos/handles (para edición Bézier futura, MVP3) — alcanza con demostrar que es POSIBLE con cada motor, no implementarlo completo.
  9. Exportar/serializar y comprobar round-trip (el SVG exportado debe ser fiel al original, o las diferencias documentadas y justificadas).
  10. Medir rendimiento con un SVG de complejidad media (definir qué es "complejidad media" en términos concretos — cantidad de paths/nodos — y documentar el método de medición).
- [ ] **Evaluación contra los 11 criterios explícitos**: fidelidad SVG, facilidad de integración con React, path/node editing, hit-testing, layers, transforms, serialization, performance, mantenibilidad, soporte TypeScript, extensibilidad — cada uno puntuado/comparado para los 3 candidatos, no solo prosa genérica.
- [ ] **Restricción arquitectónica no negociable**: el ADR debe dejar explícito que React sigue controlando layout/toolbar/inspector/layers/estado de aplicación, y que el motor gráfico se limita a Canvas/geometría interactiva — el modelo de dominio (`VectorLayer`, `ColorGroup`, etc., ya definidos en MVP2/MVP2.1) sigue siendo la fuente de verdad, el motor gráfico nunca la reemplaza. Si algún candidato hiciera esto difícil de respetar, es una desventaja real a documentar, no algo a ignorar.
- [ ] **ADR (Architecture Decision Record)**: documento formal con contexto, candidatos evaluados, matriz de decisión (criterios × candidatos, con puntuación o al menos ranking explícito), la demo ejecutable como evidencia, y una recomendación final justificada — un único ganador claro, no "cualquiera de los tres serviría".
- [ ] **NO construir el editor completo**: el spike vive en su propio espacio (ej. una carpeta de prototipo/demo separada, o una ruta de desarrollo aislada, no integrada al flujo de producción de `frontend/src/App.tsx`) — no debe tocar ni romper nada del pipeline ya existente y funcionando.

## Referencias visuales / de marca
MISSING — no hay links ni adjuntos en la tarjeta.

## Umbrales de calidad
No aplican Lighthouse/axe de la forma usual (no es una feature de producto). El estándar acá es la CALIDAD DE LA EVIDENCIA: la demo debe ser genuinamente ejecutable (no pseudocódigo), y la medición de performance debe ser real (números concretos, no estimaciones). Los test suites existentes (`dotnet test`/`pytest`/`npm test`) deben seguir en verde ya que el spike no debe tocar código de producción — confirmar que el spike vive aislado y no rompe nada.

## Ambigüedades detectadas
- **Dónde vive el código del spike**: no especificado. Recomendación del orquestador: una carpeta nueva y claramente separada (ej. `frontend/spike-editor-engine/` o similar, fuera de `frontend/src/`) para que quede evidente que es exploratorio y no se integra al build de producción — el implementador decide la ubicación exacta y la documenta.
- **"SVG de complejidad media" para el benchmark de performance**: no cuantificado — el implementador elige un valor concreto (ej. rango de cantidad de paths/nodos) y lo documenta con justificación.
- **Cuarto candidato**: la tarjeta permite explícitamente proponer uno si se justifica — no es obligatorio, el implementador puede limitarse a los 3 nombrados si no encuentra una razón de peso para agregar otro.
- Ninguna otra ambigüedad bloqueante.

## Nota de orquestación
Quinta tarjeta de MVP 2.1, y la primera de esta tarjeta en adelante que es un SPIKE de investigación en vez de una feature — el criterio de "revisar código antes de confiar" para el orquestador acá se traduce en: correr/inspeccionar la demo ejecutable, no solo leer el ADR. Corrida en modo autónomo (mismo criterio ya establecido). Recordatorio: correr `npm run build`/`npm test` sobre el código de PRODUCCIÓN para confirmar que el spike no lo rompió, aunque el spike en sí no tenga (o tenga sus propios) tests formales.
