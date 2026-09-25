# M1-S09 · Dimensiones reales en milímetros
URL: https://app.notion.com/p/3c1d77b2639881678c87c628217bb14e
Stack declarado: MISSING (no existe propiedad "Tecnología"/"Stack" en el board) — DERIVADO del cuerpo de la tarjeta y de la propiedad "Área" (Frontend, Vector, Laser): **ASP.NET Core/Vector** (persiste dimensiones físicas, valida valores, actualiza metadata SVG) · **React** (inputs numéricos con unidades, lock aspect ratio, preview de tamaño final) · **Python sin indicación explícita** — a evaluar por el implementador (probablemente NO necesita tocar Python: el cambio de tamaño físico de un SVG es una operación sobre metadata del elemento raíz `<svg>` — `width`, `height`, `viewBox`, `preserveAspectRatio` — sin reescribir los `d` de los paths, ver "Reglas" del cuerpo: "sin deformar paths salvo que el usuario lo pida"). Continúa sobre M1-S01 a M1-S08 ya mergeados en `main` (más el fix de `/api/v1/info`/README de la ronda de correcciones sobre M1-S07, PR pendiente de merge).

Nota de alcance: como las tarjetas anteriores de este board, es una feature de la aplicación de vectorización, no un sitio web de marketing. Mismo acuerdo ya establecido con el usuario: se implementa directamente con `web-implementer` (build/tests locales), sin `web-design-architect` ni `web-qa-auditor`.

## Requerimiento (transcripción del cuerpo de la tarjeta)

### Contexto
Pasar de píxeles/unidades abstractas a dimensiones físicas útiles para fabricación.

### Usuario podrá
Definir ancho o alto en mm, bloquear/desbloquear proporción y ver tamaño final antes de exportar.

### React
Inputs numéricos con unidades, lock aspect ratio y preview de dimensiones.

### ASP.NET Core/Vector
Persistir dimensiones físicas, validar valores y actualizar metadata SVG (`width`, `height`, `viewBox`) sin deformar paths salvo que el usuario lo pida.

### Reglas
Unidad interna documentada; conversiones deterministas; evitar depender del DPI ambiguo del raster para tamaño final.

### Pruebas
Aspect ratios distintos, valores límite, round-trip SVG y verificación de medidas.

### Definition of Done
El SVG exportable representa las dimensiones físicas seleccionadas y reabre conservándolas.

### Fuera de alcance
Kerf/material/máquina y nesting.

## Criterios de aceptación

De la propiedad "Criterio de aceptación": "Usuario define ancho/alto en mm; SVG conserva escala y aspect ratio según configuración."

Ampliados por "Reglas", "Pruebas" y "Definition of Done" del cuerpo (declarados explícitamente, no derivados):
- [ ] Usuario puede definir ancho O alto en mm (no necesariamente ambos); con aspect ratio bloqueado (default), el otro valor se calcula automáticamente para mantener la proporción original del diseño.
- [ ] Usuario puede desbloquear la proporción y definir ambos valores independientemente — esto SÍ deforma el diseño (estiramiento no uniforme), permitido explícitamente ("salvo que el usuario lo pida").
- [ ] Preview del tamaño final visible antes de "aplicar"/exportar (el spec no aclara si esto requiere una llamada a la Web API o puede calcularse enteramente en el cliente ya que es aritmética simple de escala — el implementador decide, documentando la elección; dado que es una operación puramente de metadata/escala, sin llamada a un motor externo, es razonable calcular el preview 100% en el cliente sin round-trip HTTP).
- [ ] "Aplicar" persiste las dimensiones físicas: actualiza `width`/`height`/`viewBox` del SVG resultante (posiblemente `preserveAspectRatio` si se permite deformación) SIN reescribir los `d` de los `<path>` — es una transformación puramente de metadata del elemento raíz, no de geometría interna.
- [ ] Unidad interna documentada: el implementador debe declarar explícitamente qué representa 1 unidad del `viewBox`/coordenadas actuales del SVG (ej. "1 unidad = 1 px del raster de origen, sin asumir ningún DPI") y cómo se deriva el factor de escala mm↔unidad interna a partir del ancho/alto en mm que pide el usuario y el tamaño actual del `viewBox`.
- [ ] Conversiones deterministas: mismo SVG de origen + mismas dimensiones en mm → mismo resultado, siempre.
- [ ] NO depender del DPI del archivo raster original para calcular el tamaño físico — el tamaño en mm lo define el usuario explícitamente, nunca se infiere de metadata EXIF/DPI del PNG/JPG original (ambigua/no confiable, como ya se decidió no usar en M1-S02).
- [ ] Round-trip: el SVG exportado, reabierto (ej. en un visor externo o vuelto a cargar en la app), debe seguir representando las mismas dimensiones físicas — esto favorece que `width`/`height` del SVG raíz usen unidades explícitas (`mm`) en vez de números sin unidad (que un visor interpretaría como píxeles CSS, no milímetros).
- [ ] Casos de prueba obligatorios: aspect ratios distintos (incluyendo 1:1, muy ancho, muy alto), valores límite (mínimo/máximo permitido, y valores inválidos como 0/negativos), round-trip (exportar y volver a leer el SVG, verificar que las dimensiones coinciden), verificación de medidas (el `viewBox` y el `width`/`height` en mm son matemáticamente consistentes entre sí).

## Referencias visuales / de marca
MISSING — no hay links ni adjuntos en la tarjeta.

## Umbrales de calidad
No aplican Lighthouse/axe por defecto del sistema en el sentido de "sitio de marketing". Verificación sustituta: ciclo local de `web-implementer` (build/typecheck/lint/tests) contra "Pruebas" y "Definition of Done" de arriba, igual que en tarjetas anteriores.

## Ambigüedades detectadas
- Propiedad "Tecnología" no existe en el board — no bloqueante, stack derivado del cuerpo y del campo "Área".
- No está claro si esta operación necesita involucrar al motor Python en absoluto, dado que es puramente metadata/escala del SVG raíz — no bloqueante, el implementador decide (razonable: mantenerlo 100% en ASP.NET Core + React, sin tocar Python, ya que no hay ningún cálculo de imagen/geometría compleja involucrado).
- No está claro si "aplicar" dimensiones necesita su propio tipo de versión persistida (mismo patrón que `SimplificationVersion`) o si alcanza con actualizar in-place la versión de origen (vector o simplificación) — no bloqueante; dado que el Definition of Done exige que el SVG "reabre conservando" las dimensiones, y el resto del proyecto usa consistentemente un patrón de historial versionado inmutable (nunca sobrescribe), lo razonable es seguir el mismo patrón (nueva versión "dimensionada" con su propio ID), que el implementador debe documentar como decisión explícita.
- Rango válido de mm (mínimo/máximo) no cuantificado — no bloqueante, el implementador elige valores razonables (ej. mínimo 1mm, máximo algo compatible con el área de trabajo típica de una cortadora láser de escritorio, ej. 1000mm) y los documenta como supuesto.
- No está claro sobre qué fuente opera (un `VectorVersion` de M1-S05 o también una `SimplificationVersion` de M1-S07) — no bloqueante, mismo criterio ya aplicado en M1-S08 (`CheckSourceKind`): aceptar ambos.
- Ninguna otra ambigüedad bloqueante.
