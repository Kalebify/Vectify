# M2-S07 · Operación por color: corte/grabado
URL: https://app.notion.com/p/3c1d77b263988164bdebcbc507a835d0

Séptima y última tarjeta de MVP2. Convierte cada capa de color (M2-S02) en una instrucción SEMÁNTICA de fabricación: Corte, Grabado o Ignorar. Es la tarjeta más simple de MVP2 — pura metadata, no toca geometría en absoluto (similar en espíritu a M1-S09 Dimensiones: metadata sobre un recurso ya existente, sin regenerar nada).

Stack: React (selector, filtros, resumen) + ASP.NET Core (persistir `ManufacturingOperation` por capa/versión).

## Requerimiento (transcripción del cuerpo de la tarjeta)

### Contexto
Convertir layers de color en instrucciones semánticas de fabricación.

### Usuario podrá
Asignar a cada layer `Corte`, `Grabado` o `Ignorar`, cambiarlo y ver una leyenda clara.

### React
Selector por layer, filtros por operación y resumen antes de exportar.

### ASP.NET Core
Persistir `ManufacturingOperation` por layer/version; validar valores; incluirlos en export metadata futura.

### Reglas
El color original identifica layer, pero la operación es independiente del color. Cambiar operación no cambia geometría.

### Pruebas
Persistencia, cambios, layers ignoradas y combinación corte+grabado.

### Definition of Done
Cada capa tiene una intención de fabricación explícita y persistente.

### Fuera de alcance
Potencia, velocidad y configuración específica de máquina.

## Criterios de aceptación

De la propiedad "Criterio de aceptación": "Cada capa puede marcarse como corte, grabado o ignorar y la decisión persiste."

Ampliados por el cuerpo de la tarjeta:
- [ ] Cada `VectorLayer` (M2-S02, identificado por su `groupId`/`VectorId` dentro de un `VectorLayerSetVersion`) puede marcarse con exactamente uno de tres valores: `Corte`, `Grabado`, `Ignorar`. El valor por defecto para una capa sin asignación explícita debe decidirse y documentarse (recomendación: sin asignar = tratarla como pendiente/sin decidir, no asumir un default silencioso como "Corte", para no fabricar por error algo que el usuario no confirmó).
- [ ] Cambiar la operación de una capa NO modifica geometría, NO crea una nueva `VectorVersion`/`VectorLayerSetVersion` — es metadata pura. Backend: nuevo modelo `ManufacturingOperation` (o similar) persistido POR capa (`groupId`) Y por versión del conjunto de capas (`VectorLayerSetVersion`/paletteId+version, consistente con cómo M2-S05 vincula un `ComponentGroup` a una `ComponentSetVersion` específica) — si el conjunto de capas se regenera (M2-S02 recalculado), las asignaciones viejas no se migran automáticamente (mismo criterio ya usado en M2-S05 para `ComponentGroup`/`ComponentSetVersion`).
- [ ] Validación de valores: solo se aceptan los 3 valores del enum (`Corte`/`Grabado`/`Ignorar`), cualquier otro valor es rechazado explícitamente.
- [ ] Persistencia versionada: cada cambio de asignación crea una versión nueva del conjunto de asignaciones (mismo patrón inmutable ya usado en todo el pipeline), nunca muta una existente.
- [ ] Frontend: selector por capa (en el panel Layers ya existente de M2-S02/M2-S04/M2-S05, no un panel paralelo) para elegir Corte/Grabado/Ignorar; filtros que permitan ver solo las capas de una operación dada (ej. "mostrar solo Corte"); un resumen ("N capas en Corte, M en Grabado, K ignoradas, J sin asignar") visible antes de exportar — el propio spec dice "resumen antes de exportar", así que debe integrarse de forma visible cerca o dentro del panel de exportación (M1-S10) sin necesariamente modificar el export en sí (el cuerpo dice "incluirlos en export metadata FUTURA" — es decir, esta tarjeta prepara los datos, pero NO es requisito de esta tarjeta modificar el endpoint de exportación real para que efectivamente incluya esta metadata en el SVG exportado; alcanza con que el dato exista, esté persistido y se pueda leer/mostrar).
- [ ] Leyenda clara: indicación visual (color/ícono/texto) de qué operación tiene cada capa, consistente con el resto de la UI ya establecida (swatches de color, etc.).
- [ ] Tests: persistencia (asignar y volver a leer da el mismo valor), cambios (reasignar una capa ya asignada crea una versión nueva con el nuevo valor, la anterior queda en el historial), capas ignoradas (marcar Ignorar y verificar que se refleja correctamente en el resumen/filtro), combinación corte+grabado (un conjunto con capas de ambos tipos simultáneamente, verificar que el resumen/filtro las distingue correctamente).
- [ ] Fuera de alcance: NO implementar potencia, velocidad, ni ninguna configuración específica de máquina/láser — eso queda fuera de MVP2 por completo (no solo de esta tarjeta).

## Referencias visuales / de marca
MISSING — no hay links ni adjuntos en la tarjeta.

## Umbrales de calidad
No aplican Lighthouse/axe. Se mantiene el estándar ya usado: `dotnet build` sin errores/warnings nuevos, los test suites relevantes en verde.

## Ambigüedades detectadas
- Valor por defecto para una capa sin asignación explícita: no especificado. Recomendación del orquestador (no vinculante): "sin asignar" como estado propio y distinto de los 3 valores (o nullable), nunca asumir "Corte" por defecto silenciosamente — el implementador decide y documenta.
- "Incluirlos en export metadata futura": confirma explícitamente que esta tarjeta NO necesita modificar el endpoint de exportación de M1-S10 — solo persistir el dato de forma que una tarjeta futura (fuera de MVP2) pueda consumirlo. No agregar esa integración a menos que sea trivial y no arriesgue nada ya construido.
- Ninguna otra ambigüedad bloqueante.

## Nota de orquestación
Última tarjeta del backlog de MVP2. Corrida en modo autónomo (sin confirmación por paso, incluido merge de PR), por autorización explícita del usuario. Al cerrar esta tarjeta, todo el backlog de MVP2 (M2-S01 a M2-S07) queda completo.
