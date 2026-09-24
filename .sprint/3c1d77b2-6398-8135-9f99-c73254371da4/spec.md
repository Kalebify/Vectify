# M1-S05 · Vectorización raster → SVG
URL: https://app.notion.com/p/3c1d77b2639881359f99c73254371da4
Stack declarado: MISSING (no existe propiedad "Tecnología"/"Stack" en el board) — DERIVADO del cuerpo de la tarjeta: **React** (acción "Vectorizar", estado processing/success/error, render del SVG) · **ASP.NET Core Web API** (crea job/operación, envía referencia de máscara a Python, valida respuesta, persiste `VectorVersion`, idempotencia) · **Python/FastAPI + motor de trazado** (integra el motor B/N, normaliza SVG, devuelve estadísticas). Continúa sobre M1-S01+M1-S02+M1-S03+M1-S04, rama base `sprint/3c1d77b2-threshold-bn`.

**Decisión bloqueante resuelta con el usuario**: la tarjeta dice literalmente "Integrar motor B/N seleccionado tras spike (Potrace/VTracer según resultado)". No existe ninguna tarjeta de spike en el board ni ningún comentario que registre esa decisión — se buscó en todo el workspace de Notion antes de preguntar. Se consultó al usuario en el chat (no se adivinó): **motor elegido = VTracer** (paquete PyPI con wheels precompilados, sin necesitar compilar contra libpotrace+agg en Windows). Debe quedar encapsulado detrás de una interfaz, tal como pide la tarjeta, para que cambiarlo a Potrace más adelante no requiera tocar React ni el contrato de la API.

Nota de alcance: como las tarjetas anteriores de este board, es una feature de la aplicación de vectorización, no un sitio web de marketing. Mismo acuerdo ya establecido con el usuario: se implementa directamente con `web-implementer` (build/tests locales), sin `web-design-architect` ni `web-qa-auditor`.

## Requerimiento (transcripción del cuerpo de la tarjeta)

### Contexto
Primer resultado vectorial real del producto. La máscara B/N se convierte a SVG canónico.

### Usuario podrá
Pulsar Vectorizar, ver progreso/estado y recibir un SVG renderizable asociado al proyecto.

### React
Acción de vectorización, estado processing/success/error y render del resultado básico.

### ASP.NET Core Web API
Crear job/operación de vectorización, enviar referencia de máscara/config a Python, validar respuesta, persistir una `VectorVersion` y devolver metadatos. Mantener idempotencia razonable para reintentos.

### Python/FastAPI
Integrar motor B/N seleccionado tras spike (**VTracer**, ver decisión arriba); encapsularlo detrás de interfaz; normalizar SVG; devolver estadísticas como paths/nodos aproximados/bounds.

### Modelo canónico
SVG es la fuente vectorial maestra. No convertir a DXF para editar.

### Seguridad/robustez
Sanitizar SVG generado, límites de ejecución/tamaño y errores tipados del proceso externo.

### Pruebas
Logos/siluetas simples, agujeros internos, bordes, imágenes vacías, timeout y SVG válido.

### Definition of Done
Una máscara de referencia produce SVG válido, persistido, reproducible y visible; cambiar de motor no requiere cambiar React.

### Fuera de alcance
Edición de nodos, color, DXF e IA.

## Criterios de aceptación

De la propiedad "Criterio de aceptación": "Una imagen de prueba produce SVG válido, visible y persistido como versión del proyecto."

Ampliados por "Pruebas", "Seguridad/robustez" y "Definition of Done" del cuerpo (declarados explícitamente, no derivados):
- [ ] Botón "Vectorizar" en React dispara la operación; estados processing/success/error se muestran claramente.
- [ ] El SVG resultante se renderiza en React (visible) al terminar.
- [ ] ASP.NET Core persiste una `VectorVersion` versionada (mismo patrón de historial que preprocesamiento/threshold: cache-hit por parámetros/máscara de origen crea versión nueva sin retroceder, lock por clave evita llamadas duplicadas a Python en requests concurrentes).
- [ ] Reintentos son razonablemente idempotentes (no deberían crear vectorizaciones duplicadas para la misma máscara de origen sin cambios).
- [ ] Python encapsula VTracer detrás de una interfaz (ej. un protocolo/clase base) — el contrato hacia ASP.NET Core no debe exponer detalles específicos de VTracer, para que sea sustituible por Potrace sin tocar React ni el contrato HTTP.
- [ ] SVG generado se sanitiza (sin scripts embebidos, sin referencias externas peligrosas) antes de persistirse/devolverse.
- [ ] Límites de ejecución (timeout) y de tamaño de entrada/salida están definidos y se comunican como error tipado, no como excepción sin controlar.
- [ ] Casos de prueba: logos/siluetas simples, formas con agujeros internos (topología con paths anidados), bordes/bounds correctos, máscara vacía (sin contenido — debe manejarse como caso controlado, no como crash), timeout, y SVG resultante válido (parseable como XML/SVG).
- [ ] Determinismo/reproducibilidad: misma máscara + misma configuración → mismo SVG (dentro de lo que VTracer garantice; si VTracer no es 100% determinista bit a bit, el implementador debe declararlo como excepción y verificar al menos que la estructura/cantidad de paths sea estable).
- [ ] Estadísticas devueltas: paths, nodos aproximados, bounds.

## Referencias visuales / de marca
MISSING — no hay links ni adjuntos en la tarjeta.

## Umbrales de calidad
No aplican Lighthouse/axe (no es página de marketing). Verificación sustituta: ciclo local de `web-implementer` (build/typecheck/lint/tests, incluida validez del SVG generado y repetibilidad) contra "Pruebas" y "Definition of Done" de arriba.

## Ambigüedades detectadas
- Propiedad "Tecnología" no existe en el board — no bloqueante, stack declarado en el cuerpo.
- **Motor de vectorización no decidido por un spike inexistente — RESUELTO**: usuario eligió VTracer explícitamente en el chat (ver "Decisión bloqueante resuelta" arriba). No se adivinó.
- "Idempotencia razonable para reintentos" no define un mecanismo exacto (¿clave de idempotencia como en M1-S02? ¿dedupe por máscara de origen + config, como el caché de M1-S03/M1-S04?) — el implementador debe elegir, documentarlo, y puede reusar el patrón ya establecido (caché por parámetros + versión nueva en cada confirmación).
- Límites exactos de tamaño/tiempo no cuantificados — el implementador debe elegir valores razonables y declararlos como supuesto.
- Área de la tarjeta en Notion no incluye "Frontend", pero el cuerpo sí pide trabajo de React explícito ("## React") — se prioriza el cuerpo de la tarjeta sobre la etiqueta, coherente con el resto de las tarjetas de este sprint.
- Ninguna otra ambigüedad bloqueante.
