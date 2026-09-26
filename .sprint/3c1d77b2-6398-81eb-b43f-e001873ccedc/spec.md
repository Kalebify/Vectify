# M2-S02 · Color → capas de fabricación
URL: https://app.notion.com/p/3c1d77b2639881ebb43fe001873ccedc

Segunda tarjeta de MVP2. Consume la paleta CONFIRMADA de M2-S01 (`ColorPaletteVersion` con `IsConfirmed=true`) y convierte cada grupo de color en una capa vectorial independiente y alineada, la unidad geométrica que el resto de MVP2 (componentes, vista explotada, agrupar, unión física, operación por color) va a manipular.

Stack: Python (vectorización por máscara) + ASP.NET Core (`VectorLayer`, orquestación) + Frontend (panel Layers).

## Requerimiento (transcripción del cuerpo de la tarjeta)

### Contexto
Convertir cada grupo de color confirmado en geometría vectorial independiente para fabricación.

### Usuario podrá
Ver una capa por color, aislarla, ocultarla y comprobar qué geometría pertenece a ella.

### React
Panel Layers con color/nombre/visibilidad y canvas combinado.

### ASP.NET Core
Modelo `VectorLayer` ligado a palette group y VectorVersion; orquestar vectorización por máscara y persistir capas.

### Python
Generar máscara por color y vectorizarla de forma independiente; normalizar coordenadas para que todas las capas encajen exactamente sobre la base.

### Pruebas
Capas solapadas/no solapadas, agujeros, transparencia y alineación pixel→vector.

### Definition of Done
Cada color confirmado genera una capa vectorial aislable, alineada y persistida.

### Fuera de alcance
Agrupar/unir piezas y asignar corte/grabado (M2-S05/M2-S06/M2-S07). Tampoco distinguir "componentes" separados dentro de una misma capa de color (eso es M2-S03).

## Criterios de aceptación

De la propiedad "Criterio de aceptación": "Cada color confirmado genera una capa aislable y visible de forma independiente."

Ampliados por el cuerpo de la tarjeta:
- [ ] Precondición: solo se puede generar capas a partir de una `ColorPaletteVersion` con `IsConfirmed=true` (la de M2-S01). Si la paleta referenciada no existe o no está confirmada, error explícito (`not_found`/`palette_not_confirmed`), no vectorizar nada.
- [ ] Por cada `ColorGroup` de la paleta confirmada: Python recibe la máscara binaria de ese grupo (ya persistida por M2-S01, recuperable por `MaskStorageKey`) y la vectoriza de forma INDEPENDIENTE (misma técnica de vectorización ya usada en M1-S05, reutilizando el motor existente — no reinventar el trazado de contornos) — reutilizar `vectorization_pipeline` / lo que exponga M1-S05, no duplicar lógica de vectorización.
- [ ] Normalización de coordenadas: todas las capas resultantes deben compartir el mismo sistema de coordenadas que la imagen original (mismo `viewBox`/dimensiones), de forma que superpuestas en el mismo canvas encajen exactamente sobre la posición real de cada color en el diseño original — no un recorte/bounding-box propio por capa.
- [ ] Backend .NET: nuevo modelo `VectorLayer`, ligado a (a) el `ColorPaletteVersion`/`ColorGroup` de origen (referencia explícita al color/paleta que la generó) y (b) una `VectorVersion` (reutilizando el tipo ya existente de M1-S05, no un tipo paralelo) — cada capa ES una vectorización, con su propio path SVG, pero con metadata adicional de qué color/grupo representa.
- [ ] Backend .NET: endpoint para generar (u obtener, si ya existe para esa combinación paleta+parámetros) el conjunto completo de capas de una paleta confirmada — una operación que produce TODAS las capas de una vez (una por color), no una API por-color-individual llamada N veces por el frontend.
- [ ] Mismo patrón cache+lock+versionado que el resto del pipeline: pedir de nuevo la generación de capas para la misma paleta+parámetros debe crear una versión nueva del conjunto de capas, nunca mutar una existente ni devolver una entidad vieja re-servida como si fuera la misma.
- [ ] Frontend: panel "Layers" — lista de capas con swatch de color, nombre (heredado del grupo de color), toggle de visibilidad individual; canvas que muestra la composición combinada de las capas visibles, alineadas correctamente sobre la imagen base.
- [ ] Tests Python: capas solapadas (dos colores cuyas formas se tocan/superponen en el espacio original), capas no solapadas, agujeros (un color con topología con huecos), transparencia (grupos que vienen de zonas con alpha parcial), y verificación explícita de alineación pixel→vector (un punto conocido en la máscara original debe corresponder a un punto dentro/fuera del path vectorizado en la posición equivalente).
- [ ] Tests .NET: ciclo versión/cache/lock (mismo criterio que precedentes), validación de precondición (paleta no confirmada → error, sin llamar a Python).
- [ ] Tests de frontend: toggle de visibilidad por capa, renderizado combinado correcto de las capas visibles.
- [ ] NO agrupar ni unir piezas, NO asignar corte/grabado, NO distinguir componentes separados dentro de una capa — todo eso es de tarjetas posteriores.

## Referencias visuales / de marca
MISSING — no hay links ni adjuntos en la tarjeta.

## Umbrales de calidad
No aplican Lighthouse/axe. Se mantiene el estándar ya usado: `dotnet build` sin errores/warnings nuevos, los 4 test suites en verde antes de dar la tarjeta por terminada.

## Ambigüedades detectadas
- No se especifica si "vectorizar cada máscara independientemente" implica volver a llamar al mismo endpoint/pipeline Python que M1-S05 (`/api/v1/vectorize`) N veces (una por color) o si conviene un endpoint dedicado que reciba las N máscaras y devuelva las N capas en una sola llamada (más eficiente, evita N round-trips HTTP) — el implementador decide y documenta; dado que el patrón ya establecido prefiere pipelines Python reutilizables (`vectorization_pipeline` como función, no solo como endpoint), la opción más consistente es que el nuevo endpoint de capas invoque la función de vectorización ya existente N veces server-side dentro de una misma request .NET→Python, no N requests HTTP separadas.
- "Alineación pixel→vector" como caso de test no cuantifica una tolerancia — el implementador elige un valor razonable (ej. sub-píxel o 1px) y lo documenta, consistente con las tolerancias relativas ya usadas en M1-S08.
- Ninguna otra ambigüedad bloqueante.

## Nota de orquestación
Corrida en modo autónomo (M2-S01..M2-S07 sin confirmación por paso, incluido merge de PRs), por autorización explícita del usuario. Tarjetas que no puedan cerrarse quedan en "Bloqueada" en Notion con el motivo documentado.
