# M2-S01 · Detección y reducción de paleta de colores
URL: https://app.notion.com/p/3c1d77b2639881609ba5c926b583a39a

Primera tarjeta de MVP 2. Arranca el flujo multicapa: el color deja de ser solo apariencia y pasa a interpretarse como información potencial de fabricación (una capa por color = una operación de láser distinta más adelante, en M2-S07).

Stack: Python (detección/cuantización de color) + ASP.NET Core (persistencia/orquestación) + Frontend (paleta interactiva).

## Requerimiento (transcripción del cuerpo de la tarjeta)

### Contexto
Inicio del flujo multicapa. El color se interpreta como información potencial de fabricación, no solo como apariencia.

### Usuario podrá
Subir/usar imagen en color, ver paleta detectada, número de colores, fusionar colores parecidos, renombrar grupos y confirmar paleta.

### React
Paleta interactiva con swatches, porcentaje/área aproximada, merge/unmerge y preview del resultado cuantizado.

### ASP.NET Core
Persistir `ColorPaletteVersion`, parámetros y decisiones del usuario; orquestar Python.

### Python
Analizar transparencia/fondo y colores; implementar cuantización/cluster en espacio de color apropiado; permitir tolerancia y número objetivo; devolver máscara/estadísticas por grupo.

### Pruebas
Colores sólidos, anti-aliasing, sombras, transparencias, colores casi iguales y muchos colores.

### Definition of Done
La paleta detectada es editable y reproducible y queda confirmada como entrada del siguiente sprint (M2-S02).

### Fuera de alcance
Crear todavía las capas SVG finales (eso es M2-S02).

## Criterios de aceptación

De la propiedad "Criterio de aceptación": "Se muestra paleta detectada y usuario puede ajustar/confirmar agrupaciones."

Ampliados por el cuerpo de la tarjeta (declarados explícitamente, no derivados):
- [ ] Endpoint Python que recibe una imagen (misma imagen de entrada que ya usa el pipeline de preprocesamiento/threshold, RGBA soportado) y devuelve: paleta detectada (lista de colores dominantes), número de colores, y para cada color/grupo su máscara (o referencia recuperable) y estadísticas (al menos % de área que ocupa).
- [ ] Cuantización/clustering en un espacio de color perceptualmente razonable (no forzar RGB puro si hay una opción mejor disponible sin dependencias nuevas pesadas — el implementador decide y documenta la elección), con dos parámetros configurables: tolerancia (qué tan distintos deben ser dos colores para no fusionarse automáticamente) y número objetivo de colores (límite superior opcional).
- [ ] Manejo explícito de transparencia/fondo: un fondo transparente (alpha=0) no debe contarse como "color" de la paleta; debe poder distinguirse de un color sólido de fondo real.
- [ ] Backend .NET: nuevo modelo `ColorPaletteVersion` siguiendo el mismo patrón de versionado inmutable ya usado en el proyecto (cache+lock+historial de versiones, ver `VectorVersion`/`SimplificationVersion`/`DimensionVersion` como precedentes) — cada confirmación de paleta (o cambio de parámetros/merge) crea una nueva versión, nunca muta una existente.
- [ ] Backend .NET: endpoint(s) para (a) solicitar detección de paleta sobre una imagen ya subida, (b) fusionar (merge) dos o más grupos de color en uno, (c) deshacer un merge (unmerge) mientras la paleta no esté confirmada, (d) renombrar un grupo, (e) confirmar la paleta final. Orquesta a Python vía un cliente HTTP siguiendo el patrón ya establecido (`PythonVectorizeClient`, `PythonSimplifyClient`, `PythonCheckClient` como precedentes: validación defensiva de la respuesta antes de confiar en ella).
- [ ] Frontend: panel de paleta interactivo — swatches con color, nombre editable, porcentaje de área aproximada; selección múltiple para merge; acción de unmerge; preview del resultado cuantizado (imagen con los colores ya agrupados) actualizado en vivo tras cada cambio; acción de confirmar paleta.
- [ ] La paleta confirmada debe ser reproducible: los mismos parámetros sobre la misma imagen deben producir el mismo resultado determinísticamente (sin aleatoriedad no controlada en el clustering, o semilla fija si el algoritmo la requiere).
- [ ] Tests Python cubriendo explícitamente: colores sólidos (pocos colores, separación clara), anti-aliasing (bordes con gradiente de color), sombras (variaciones de luminosidad del "mismo" color lógico), transparencia (alpha parcial y total), colores casi iguales (deben tender a fusionarse según tolerancia) y muchos colores (imagen con alta variedad, el límite `número objetivo` debe respetarse).
- [ ] Tests .NET cubriendo el ciclo versión/cache/lock igual que los precedentes (cache hit crea versión nueva, nunca reutiliza), y validación de parámetros inválidos.
- [ ] Tests de frontend cubriendo merge/unmerge/rename/confirm y actualización del preview.
- [ ] La tarjeta NO genera capas SVG finales — solo la paleta confirmada, que queda como entrada declarada del siguiente sprint (M2-S02). No implementar vectorización por color en esta tarjeta.

## Referencias visuales / de marca
MISSING — no hay links ni adjuntos en la tarjeta (igual que en toda la MVP1, sin diseños de marca formales; se sigue el lenguaje visual ya establecido en el frontend existente).

## Umbrales de calidad
No aplican Lighthouse/axe (no es un sitio de marketing). Se mantiene el estándar ya usado en MVP1: `dotnet build` sin errores/warnings nuevos, `pytest`/`dotnet test`/`npm test` en verde antes de dar la tarjeta por terminada.

## Ambigüedades detectadas
- "Espacio de color apropiado" no se especifica (RGB vs Lab vs HSV) — decisión técnica del implementador, debe documentarse en IMPL.md con la justificación.
- "Muchos colores" como caso de prueba no cuantifica un número — el implementador elige un valor razonable (ej. 50+) y lo documenta.
- No se especifica un límite de tamaño de imagen para la paleta — se asume el mismo límite ya validado en preprocesamiento/upload existente, sin agregar uno nuevo salvo que sea necesario por performance del clustering.
- Ninguna otra ambigüedad bloqueante.

## Nota de orquestación
El usuario autorizó explícitamente correr todo el backlog de MVP2 (M2-S01 a M2-S07) sin pedir confirmación por cada paso (branch, implementación, tests, commit, push, PR, merge, Notion Doing→Done), incluyendo merge de PRs — a diferencia del criterio "nunca mergear sin instrucción explícita" usado en MVP1. Tarjetas que no puedan cerrarse por completo quedan documentadas y movidas a "Bloqueada" en Notion en vez de Done, y se reporta todo al final en un resumen.
