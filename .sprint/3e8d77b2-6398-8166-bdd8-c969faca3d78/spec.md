# M2.1-S01 · Auditoría y corrección del pipeline multicolor
URL: https://app.notion.com/p/3e8d77b263988166bdd8c969faca3d78

Primera tarjeta de MVP 2.1 — un sub-sprint insertado entre MVP2 (cerrado) y MVP3, motivado por observación funcional real del usuario: el pipeline multicolor "termina en blanco y negro". Esta tarjeta es de AUDITORÍA + corrección puntual, no una reescritura. Regla explícita del cuerpo de la tarjeta: **"No reescribir el pipeline por intuición. Primero reproducir, instrumentar, localizar la pérdida de color y después corregirla."**

## Requerimiento (transcripción del cuerpo de la tarjeta)

### Contexto
Las tarjetas de MVP 2 relacionadas con color fueron cerradas, pero en prueba funcional el usuario observa que el flujo actual termina esencialmente en blanco y negro. No asumir la causa: primero trazar el pipeline completo y localizar exactamente dónde se pierde la información de color.

### Objetivo técnico
Auditar Upload → Preprocess → Threshold/Color Quantization → Masks → Trace → SVG → Layers → Frontend. Deben existir dos rutas explícitas: **B/N** y **Multicolor**. El modo multicolor no debe pasar accidentalmente por un threshold binario destructivo.

### Trabajo requerido
- Reproducir el problema con fixtures controlados de 2, 4 y 6 colores.
- Registrar artefactos intermedios: original, imagen preprocesada, paleta, máscara por color, SVG por color y documento compuesto.
- Revisar contratos React → ASP.NET → Python y detectar defaults que fuercen B/N.
- Revisar serialización/persistencia para comprobar que colorId, fill, layerId y paletteId no se pierdan.
- Corregir únicamente la causa comprobada.
- Añadir telemetría/logs útiles sin registrar imágenes privadas.

### Dataset mínimo
1. Logo B/N.
2. Cuatro bloques RGBY con fondo blanco.
3. Ilustración infantil de 4–6 colores sólidos.
4. Imagen con antialiasing.
5. PNG con transparencia.

### Tests
Unitarios Python para extracción/preservación de color; integración ASP.NET↔Python; test frontend que compruebe que una respuesta multicolor no se renderiza como B/N; regresión del modo B/N.

### Definition of Done
Se documenta causa raíz, corrección, evidencia before/after y tests. Un fixture de cuatro colores devuelve al menos cuatro grupos/layers coherentes cuando la configuración lo solicita.

### Fuera de alcance
Editor avanzado, nodos, booleanas, IA y rediseño visual completo.

### Regla para el agente
No reescribir el pipeline por intuición. Primero reproducir, instrumentar, localizar la pérdida de color y después corregirla.

## Criterio de aceptación (propiedad Notion)
"Una imagen controlada de 4–6 colores produce una paleta multicolor verificable y conserva esos colores hasta la generación de capas, con tests de regresión para B/N."

## HALLAZGO YA CONFIRMADO POR EL ORQUESTADOR (evidencia real, no hipótesis)

Antes de delegar esta tarjeta, reproduje el problema personalmente contra el stack Docker corriendo en local (build actual de `main`, post-MVP2), con una imagen de prueba de 5 colores (fondo blanco + rojo + verde + azul + amarillo, sin antialiasing). Pasos exactos y resultado:

1. `POST /api/v1/projects` (subir la imagen de 5 colores) → 201.
2. `POST .../color-palette/detect` → **correcto**: devuelve 5 grupos con `colorHex` exacto (`#ffffff`, `#0000ff`, `#ff0000`, `#00c800`, `#ffdc00`) y `areaPercent` coherente. La detección de paleta (M2-S01 de MVP2) NO tiene el bug.
3. `POST .../color-palette/{id}/confirm` → 200, paleta confirmada.
4. `POST .../color-palette/{id}/layers` (generación de capas vectoriales, M2-S02 de MVP2) → 201, devuelve 5 `VectorLayer` con su `colorHex` correcto en la metadata JSON.
5. `GET` del SVG real de la capa "Color 3" (`colorHex` reportado: `#ff0000`) → el contenido real del archivo es:
   ```xml
   <svg xmlns="http://www.w3.org/2000/svg" version="1.1" width="400" height="300">
   <path d="M0,0 L141,0 L141,131 L0,131 Z " fill="#000000" transform="translate(10,10)" />
   </svg>
   ```
   **El path tiene `fill="#000000"` (negro), no `#ff0000`.** Se repite igual para las otras 4 capas — SIEMPRE negro, sin importar el color real del grupo.

**Causa raíz localizada con certeza:** `services/python-engine/app/core/vector_engine.py` (`VtracerEngine.trace`) llama a VTracer con `colormode="binary"` — un modo diseñado exclusivamente para el pipeline B/N de M1-S05 (donde el color es irrelevante, solo importa presencia/ausencia de material para cortar). VTracer en modo binario SIEMPRE devuelve el path con un fill fijo (negro), sin ningún concepto de "color de entrada". `VectorLayerService.cs` (M2-S02) reutiliza este MISMO motor para vectorizar cada máscara de color, **pero en ningún punto del pipeline (ni en Python `vectorize_layers`, ni en .NET `VectorLayerService.GenerateLayersAsync`) se inyecta el `ColorHex` real del grupo como atributo `fill` del `<path>` resultante.** La metadata (`ColorGroup.ColorHex`/`VectorLayer.ColorHex`) es correcta y sobrevive toda la serialización — el bug NO es de pérdida de `colorId`/`paletteId` en la serialización (esa parte funciona bien) — es que esa metadata JAMÁS SE APLICA a la geometría SVG real. Por eso:
- El panel "Paleta de colores" se ve bien (usa la metadata directamente para los swatches).
- El panel "Layers"/canvas combinado (que renderiza el SVG real de cada capa vía `<img src={getSvgUrl(vectorId)}>`) muestra TODAS las capas como siluetas negras superpuestas — indistinguible de blanco y negro, exactamente el síntoma reportado.

**Esto NO es "el modo multicolor pasa accidentalmente por un threshold binario destructivo"** (la hipótesis que el cuerpo de la tarjeta sugiere explorar): confirmé que `ColorPaletteService.DetectAsync` lee la imagen ORIGINAL directamente (nunca pasa por `ThresholdService`), y que las máscaras usadas para vectorizar cada capa son las máscaras propias de M2-S01 (clustering de color), no máscaras de threshold B/N. El pipeline de detección de color es completamente independiente y correcto; el problema es puntual y posterior: falta pintar el resultado vectorizado con el color real.

## Trabajo requerido para esta tarjeta (adaptado con el hallazgo de arriba)

- [ ] **Reproducir formalmente** con el dataset mínimo completo (2, 4 y 6 colores + los 5 fixtures listados: logo B/N, 4 bloques RGBY, ilustración 4-6 colores, imagen con antialiasing, PNG con transparencia) — no alcanza con mi reproducción de 5 colores sin antialiasing/transparencia; hay que confirmar que la MISMA causa raíz (fill negro fijo) aplica también con antialiasing (colores "casi iguales" que M2-S01 ya fusiona por tolerancia) y con transparencia (grupos con `hasPartialAlpha`). Documentar cualquier diferencia encontrada.
- [ ] **Registrar artefactos intermedios** de al menos un caso (ej. el fixture de 4 colores): imagen original, paleta detectada (JSON), máscara PNG por color, SVG por color ANTES del fix (negro) y DESPUÉS del fix (color real), documento compuesto — guardarlos como evidencia en IMPL.md o adjuntos, no solo describir en prosa.
- [ ] **Revisar contratos** React → ASP.NET → Python explícitamente, confirmando (o refutando con evidencia) que ninguno fuerza B/N por default — dado el hallazgo ya confirmado, se espera que esta revisión NO encuentre un default que fuerce B/N (la ruta multicolor es genuinamente independiente), pero hay que verificarlo y documentarlo, no asumirlo.
- [ ] **Confirmar explícitamente** que `colorId`/`paletteId`/`layerId` sobreviven la serialización completa (ya lo confirmé para `colorHex`/`groupId`/`paletteId`/`vectorId` en mi reproducción — extender la verificación si el implementador encuentra otros identificadores relevantes).
- [ ] **Corregir la causa comprobada**: inyectar el `ColorHex` real de cada `ColorGroup`/`VectorLayer` como atributo `fill` del/de los `<path>` del SVG resultante de esa capa, ANTES de persistirlo. Decisión de dónde aplicar el fix (Python al construir la respuesta de `/vectorize-layers`, o .NET en `VectorLayerService.GenerateLayersAsync` reescribiendo el XML del SVG recibido, mismo patrón que `SvgDimensionWriter` de M1-S09) queda a criterio del implementador — documentar la elección y por qué. **No modificar el pipeline B/N (M1-S03/M1-S04/M1-S05)** — esa ruta debe seguir generando SVGs sin fill forzado (o con su comportamiento actual intacto), ver "Regresión B/N" abajo.
- [ ] **Regresión B/N explícita**: correr (o agregar si no existen) tests que confirmen que el pipeline de M1 (Preprocess → Threshold → Vectorize, sin paleta de colores) sigue produciendo exactamente el mismo resultado que antes de este fix — el cambio debe ser aditivo y aislado a la ruta multicolor (M2-S02), nunca tocar `VtracerEngine`/`VectorizationService` compartido de forma que afecte al flujo B/N existente. Si el fix requiere tocar código compartido, justificar por qué es seguro y agregar el test de regresión correspondiente.
- [ ] **Telemetría/logs**: agregar logging útil en el punto donde se aplica el color (ej. "capa {GroupId} pintada con fill {ColorHex}"), sin loggear contenido de imágenes.
- [ ] **Tests**:
  - Python: unitario que verifique que, dado un color de entrada conocido, el SVG resultante de vectorizar su máscara tiene el `fill` correcto (si el fix se aplica en Python) o que el contrato de `/vectorize-layers` devuelve lo necesario para que .NET lo aplique (si el fix se aplica en .NET).
  - .NET: integración que confirme que `VectorLayerService.GenerateLayersAsync` persiste un SVG con `fill` igual al `ColorHex` del grupo, para cada capa generada — replicando mi reproducción manual (4-6 colores → cada capa con su color real, no negro).
  - Frontend: test que confirme que una respuesta multicolor NO se renderiza como si fuera B/N (ej. snapshot/assert de que el `<img>` de cada capa apunta a un SVG con el fill esperado, o test de integración sobre el panel Layers con capas de distinto color).
  - Regresión B/N: test explícito (nuevo o ya existente re-verificado) de que el pipeline M1 sigue igual.
- [ ] **Definition of Done verificable**: un fixture de 4 colores (ej. RGBY) debe devolver AL MENOS 4 grupos/capas coherentes, cada una con su color real aplicado al SVG (no solo en la metadata JSON) — replicar mi reproducción con un fixture de 4 colores como parte de la evidencia final.

## Referencias visuales / de marca
MISSING — no hay links ni adjuntos en la tarjeta.

## Umbrales de calidad
No aplican Lighthouse/axe. Estándar ya usado: build sin errores/warnings nuevos, los test suites relevantes en verde (incluyendo el suite completo de MVP1/MVP2, no solo los tests nuevos, para confirmar que no hay regresión).

## Ambigüedades detectadas
- Dónde aplicar el fix exacto (Python vs .NET) — ver arriba, decisión del implementador con justificación documentada. Recomendación del orquestador (no vinculante): aplicarlo en .NET (`VectorLayerService.GenerateLayersAsync`), reescribiendo el `fill` del SVG ya recibido de Python vía `System.Xml.Linq` — mismo patrón ya usado y probado por `SvgDimensionWriter` (M1-S09), sin tocar el contrato HTTP de Python ni el motor compartido `VtracerEngine`.
- Qué hacer con paths que tengan subpaths anidados (agujeros, `hierarchical="stacked"`) — el `fill` debe aplicarse al `<path>` completo (con su regla de winding/evenodd existente para agujeros), no por subpath individual.
- Ninguna otra ambigüedad bloqueante.

## Nota de orquestación
El usuario pidió continuar en modo similar al backlog de MVP2 (batch M2.1-S01 a M2.1-S08). Interpreto que esto mantiene el mismo criterio de autonomía ya establecido explícitamente para MVP2 (branch → implementación → revisión personal → tests independientes → commit → push → PR → merge → Notion Done, sin pedir confirmación por paso) — lo marco explícitamente por si no era la intención, igual que hice al arrancar MVP2. Dado que esta tarjeta es una auditoría con hallazgo ya confirmado por mí mismo (ver arriba), el rol del implementador es: verificar/ampliar la reproducción con el dataset completo, aplicar el fix, y escribir los tests — no re-descubrir la causa raíz desde cero, aunque debe validarla independientemente antes de confiar en mi hallazgo.
