# M1-S10 · Exportación SVG
URL: https://app.notion.com/p/3c1d77b2639881c99b21cbc22b623603
Stack declarado: MISSING (no existe propiedad "Tecnología"/"Stack" en el board) — DERIVADO del cuerpo de la tarjeta y de la propiedad "Área" (ASP.NET Core, Vector, Laser): **ASP.NET Core** (endpoint de export, serialización/sanitización de nombre de archivo, headers, auditoría básica) · **React** (diálogo/resumen de exportación con versión, tamaño, issues del checker, botón de descarga) · **Python sin indicación explícita** — no debería hacer falta tocarlo: el SVG a exportar ya es un artefacto persistido y sanitizado por una etapa anterior (Vectorización M1-S05, Simplificación M1-S07, o Dimensionamiento M1-S09), este sprint solo lo SIRVE con los headers/nombre correctos, no genera geometría nueva. Continúa sobre M1-S01 a M1-S09 ya mergeados en `main` (más los fixes de `/api/v1/info`/README, PR #10, pendiente de merge).

Nota de alcance: como las tarjetas anteriores de este board, es una feature de la aplicación de vectorización, no un sitio web de marketing. Mismo acuerdo ya establecido con el usuario: se implementa directamente con `web-implementer` (build/tests locales), sin `web-design-architect` ni `web-qa-auditor`.

## Requerimiento (transcripción del cuerpo de la tarjeta)

### Contexto
Cerrar el primer flujo productivo: obtener un archivo SVG limpio fuera de la aplicación.

### Usuario podrá
Elegir versión, revisar dimensiones/avisos y descargar SVG.

### React
Diálogo/resumen de exportación con versión, tamaño, issues del checker y acción de descarga.

### ASP.NET Core
Endpoint de export; serialización/sanitización; nombre de archivo; headers; auditoría básica de versión exportada.

### Vector
Preservar viewBox, paths, fills/strokes necesarios y dimensiones físicas; evitar metadata basura del proceso.

### Pruebas
Reabrir SVG exportado, comparar bounds/path count y medidas; nombres con caracteres especiales; export repetido.

### Definition of Done
SVG descargado es válido, autosuficiente, conserva geometría/dimensiones y corresponde exactamente a una versión del proyecto.

### Fuera de alcance
DXF, PDF y parámetros de potencia/velocidad.

## Criterios de aceptación

De la propiedad "Criterio de aceptación": "SVG exportado reabre correctamente y conserva geometría y dimensiones."

Ampliados por "Pruebas" y "Definition of Done" del cuerpo (declarados explícitamente, no derivados):
- [ ] Usuario elige QUÉ versión exportar: el spec no limita esto a una sola etapa del pipeline — "elegir versión" es genérico. Se acepta como origen un `VectorVersion` (M1-S05), una `SimplificationVersion` (M1-S07) o una `DimensionVersion` (M1-S09), mismo criterio dual/triple ya usado en M1-S08/M1-S09 (`sourceKind`).
- [ ] Diálogo de exportación muestra: versión elegida, tamaño (dimensiones físicas en mm si existe una `DimensionVersion`, o las dimensiones en unidades internas/px si no), y un resumen de issues del Laser Checker (M1-S08) — el checker sigue siendo de solo lectura/informativo, NO bloquea la descarga aunque haya issues (consistente con que M1-S08 nunca corrige ni impide nada automáticamente).
- [ ] Descarga real del archivo con `Content-Disposition: attachment` y un nombre de archivo sanitizado (sin caracteres inválidos para sistemas de archivos/headers HTTP) — caso de prueba explícito: "nombres con caracteres especiales".
- [ ] El SVG descargado es exactamente el mismo artefacto ya persistido por la etapa de origen (Vectorización/Simplificación/Dimensionamiento) — el endpoint de export NO reescribe ni modifica la geometría, solo sirve los bytes existentes con los headers correctos (spec: "corresponde exactamente a una versión del proyecto").
- [ ] "Auditoría básica de versión exportada": no cuantificado qué tan persistente debe ser — como mínimo, logging estructurado (`ILogger`) de cada export (versión, proyecto, imagen, timestamp); el implementador decide si además amerita un registro persistido (sidecar JSON) y lo documenta como supuesto explícito.
- [ ] Export repetido de la misma versión produce el mismo archivo (determinismo) y no falla ni genera un estado inconsistente.
- [ ] Casos de prueba obligatorios: reabrir el SVG exportado y comparar bounds/pathCount/medidas contra la versión de origen (deben coincidir exactamente), nombres de archivo con caracteres especiales (ej. tildes, espacios, símbolos del nombre del proyecto/imagen original), export repetido.

## Referencias visuales / de marca
MISSING — no hay links ni adjuntos en la tarjeta.

## Umbrales de calidad
No aplican Lighthouse/axe por defecto del sistema en el sentido de "sitio de marketing". Verificación sustituta: ciclo local de `web-implementer` (build/typecheck/lint/tests) contra "Pruebas" y "Definition of Done" de arriba, igual que en tarjetas anteriores.

## Ambigüedades detectadas
- Propiedad "Tecnología" no existe en el board — no bloqueante, stack derivado del cuerpo y del campo "Área".
- Nivel de persistencia de la "auditoría básica" no cuantificado — no bloqueante, el implementador decide (mínimo: logging estructurado; opcional: registro persistido) y documenta.
- No está claro si el checker (M1-S08) se re-ejecuta automáticamente al abrir el diálogo de exportación o si reutiliza un análisis ya corrido por el usuario en la sección del Laser Checker — no bloqueante, el implementador decide (razonable: re-ejecutarlo a pedido, mismo criterio de disparo manual ya usado en Check/Simplify/Dimensions, evitando forzar un análisis que el usuario no pidió).
- Esquema de nombre de archivo no especificado — no bloqueante, el implementador elige un esquema razonable (ej. basado en el nombre del proyecto/imagen original + sufijo de versión/etapa) y lo sanitiza, documentando el criterio. El nombre original del archivo subido ya existe en `ProjectRecord.FileName` (`backend/Vectify.Api/Projects/ProjectRecord.cs`) — es la fuente natural del nombre base, y explica por qué "nombres con caracteres especiales" es un caso de prueba explícito (el usuario controla ese nombre al subir la imagen en M1-S02, puede contener tildes/espacios/símbolos que rompan un `Content-Disposition` sin sanitizar, ver RFC 6266 para el caso no-ASCII).
- Ninguna otra ambigüedad bloqueante.
