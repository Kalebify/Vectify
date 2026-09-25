# M1-S08 · Paths abiertos y líneas duplicadas
URL: https://app.notion.com/p/3c1d77b2639881af8467ec3700465e9c
Stack declarado: MISSING (no existe propiedad "Tecnología"/"Stack" en el board) — DERIVADO del cuerpo de la tarjeta y de la propiedad "Área" (Vector, Laser): **ASP.NET Core/Vector** (servicio de validación/análisis geométrico, resultados tipados con coordenadas/IDs, tolerancias en unidades del modelo) · **React** (panel de issues con severidad, filtros y highlight en canvas, reutilizando `VectorCanvas`/`useCanvasTransform` de M1-S06) · **Python sin indicación explícita** — a evaluar por el implementador si el análisis geométrico conviene hacerlo en Python (junto al resto del procesamiento de paths, ej. reutilizando el parseo de `simplification_pipeline.py` de M1-S07) o directamente en C# (dato ya disponible como XML/paths parseados). Continúa sobre M1-S01 a M1-S07 ya mergeados en `main`.

Nota de alcance: como las tarjetas anteriores de este board, es una feature de la aplicación de vectorización, no un sitio web de marketing. Mismo acuerdo ya establecido con el usuario: se implementa directamente con `web-implementer` (build/tests locales), sin `web-design-architect` ni `web-qa-auditor`.

## Requerimiento (transcripción del cuerpo de la tarjeta)

### Contexto
Primer Laser Checker: detectar geometría que puede producir cortes inesperados.

### Usuario podrá
Ejecutar análisis, ver número de paths abiertos y duplicados y hacer clic en un problema para resaltarlo.

### React
Panel de issues con severidad, filtros y highlight en canvas.

### ASP.NET Core/Vector
Servicio de validación con resultados tipados y coordenadas/IDs. Definir tolerancias en unidades del modelo.

### Detecciones
Paths que deberían estar cerrados pero no lo están; segmentos/paths coincidentes dentro de tolerancia; no eliminar automáticamente todavía.

### Pruebas
Duplicado exacto, casi duplicado, path abierto pequeño/grande, diseños correctos y falsos positivos conocidos.

### Definition of Done
Checker produce resultados reproducibles y localizables visualmente sin modificar el SVG.

### Fuera de alcance
Autocorrección, bridges, kerf y validación completa de fabricación.

## Criterios de aceptación

De la propiedad "Criterio de aceptación": "El análisis identifica paths abiertos y duplicados y los localiza visualmente."

Ampliados por "Pruebas" y "Definition of Done" del cuerpo (declarados explícitamente, no derivados):
- [ ] Análisis se ejecuta a pedido del usuario (no automático en cada cambio) — mismo criterio de disparo manual ya usado en Vectorize/Simplify.
- [ ] Detecta paths que "deberían estar cerrados pero no lo están": el spec no define el criterio geométrico exacto de "debería" — el implementador debe definir una heurística razonable (ej. start point y end point del path están dentro de una tolerancia pequeña pero no hay comando `Z`) y documentarla como supuesto explícito.
- [ ] Detecta segmentos/paths coincidentes dentro de tolerancia: duplicado exacto (mismas coordenadas) y casi-duplicado (dentro de una tolerancia configurable en unidades del modelo, no en píxeles de pantalla).
- [ ] NO corrige nada automáticamente — el checker es de solo lectura, nunca modifica el SVG (Definition of Done explícito).
- [ ] Resultados son reproducibles: mismo SVG + mismas tolerancias → mismos issues, mismo orden.
- [ ] Cada issue devuelto es localizable visualmente: coordenadas y/o IDs de path suficientes para que React pueda resaltarlo en el canvas.
- [ ] React: panel de issues con severidad (el spec no define niveles — el implementador decide, ej. warning/error, y documenta), filtros (por tipo de issue como mínimo: abiertos vs duplicados), y click-to-highlight sobre el `VectorCanvas` ya existente (M1-S06).
- [ ] Casos de prueba obligatorios: duplicado exacto, casi-duplicado (dentro y fuera de tolerancia — un caso límite justo debajo y justo arriba del umbral), path abierto chico, path abierto grande, diseños correctos (sin ningún issue, cero falsos positivos), y casos conocidos de falso positivo (el spec asume que existen casos límite donde el checker podría equivocarse — el implementador debe identificar al menos uno y verificar que no se dispara indebidamente, o documentar por qué no aplica).
- [ ] Tolerancias definidas en unidades del modelo (no píxeles de pantalla ni relativas al zoom del canvas) — consistente con que el análisis debe ser independiente de cómo se esté visualizando.

## Referencias visuales / de marca
MISSING — no hay links ni adjuntos en la tarjeta.

## Umbrales de calidad
No aplican Lighthouse/axe por defecto del sistema en el sentido de "sitio de marketing". Verificación sustituta: ciclo local de `web-implementer` (build/typecheck/lint/tests) contra "Pruebas" y "Definition of Done" de arriba, igual que en tarjetas anteriores.

## Ambigüedades detectadas
- Propiedad "Tecnología" no existe en el board — no bloqueante, stack derivado del cuerpo y del campo "Área" ("Vector", "Laser" — sin "Python" ni "Frontend" explícitos esta vez, a diferencia de M1-S07; "React" y "ASP.NET Core/Vector" sí están en el cuerpo).
- No está definido si el análisis opera sobre el `VectorVersion` original (M1-S05) o también/en cambio sobre una `SimplificationVersion` (M1-S07) si existe una — no bloqueante, el implementador decide (razonable: aceptar cualquiera de los dos tipos de origen, ya que ambos son SVG con paths M/L/Z) y lo documenta.
- Criterio geométrico exacto de "path que debería estar cerrado" no está definido — no bloqueante, el implementador define una heurística (ej. distancia entre extremos) y la documenta como supuesto.
- Niveles de severidad no están definidos — no bloqueante, el implementador decide y documenta.
- Valor(es) de tolerancia por defecto no están cuantificados — no bloqueante, mismo criterio que los presets de M1-S07 (el implementador elige y documenta).
- "Casos conocidos de falso positivo" es vago — no bloqueante, el implementador interpreta y documenta su interpretación.
- Ninguna otra ambigüedad bloqueante.
