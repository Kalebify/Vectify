# M2.1-S03 · Color → Layers vectoriales independientes
URL: https://app.notion.com/p/3e8d77b263988106bca8ec23a8ac4946

Tercera tarjeta de MVP 2.1. Como M2.1-S02, esta tarjeta ENDURECE/COMPLETA lo ya construido en M2-S02 de MVP2 (`VectorLayerService`, generación de una capa vectorial por color) — no lo reescribe. El pipeline "paleta confirmada → máscara por color → tracing → SVG → Layer" ya existe y ya funciona (y desde M2.1-S01 cada capa tiene su color real pintado). El trabajo NUEVO de esta tarjeta es: (a) formalizar el modelo `VectorLayer` con los campos que hoy están dispersos en servicios separados, y (b) agregar una VALIDACIÓN nueva que hoy no existe: comparar la máscara raster original contra la geometría vectorial resultante, con tolerancia, detectando contaminación cruzada entre colores.

Stack: Python (limpieza de máscara si hace falta) + Vector + ASP.NET Core (modelo/validación).

## Requerimiento (transcripción del cuerpo de la tarjeta)

### Objetivo (propiedad Notion)
Generar geometría vectorial independiente para cada color confirmado manteniendo coordenadas comunes y trazabilidad.

### Criterio de aceptación (propiedad Notion)
Cada color confirmado genera un Layer con geometría propia; aislar ese Layer muestra únicamente las regiones correspondientes a ese color.

### Regla de dominio
**Color, Layer, Path, Component y Group son conceptos diferentes.** Un color confirmado origina normalmente un Layer; el Layer contiene paths; los paths pueden formar varios componentes físicos.

### Pipeline
Paleta confirmada → máscara por color → limpieza de máscara → tracing → normalización SVG → Layer → componentes.

### Modelo mínimo
`VectorLayer: id, name, colorId, fill, visible, locked, order, manufacturingOperation, paths[], componentCount`.

### Requisitos
Todos los layers comparten viewBox y sistema de coordenadas. Reordenar un layer no debe alterar su geometría. Los IDs deben persistir.

### Validación
Comparar máscara raster y geometría resultante. Permitir tolerancia, pero evitar que regiones de otro color aparezcan dentro del layer seleccionado.

### Tests
Cuatro colores, huecos, componentes separados del mismo color, transparencia y colores contiguos.

### Definition of Done
El documento compuesto reproduce razonablemente la imagen y cada layer puede renderizarse solo sin depender de los demás.

### Fuera de alcance
Herramientas manuales del editor.

## Estado actual (ya construido — punto de partida)

Ya existe y funciona: `VectorLayerService.GenerateLayersAsync` (M2-S02/MVP2) genera una `VectorVersion` por color, ya pintada con su color real (`SvgFillWriter`, M2.1-S01), compartiendo `sourceWidthPx`/`sourceHeightPx`/viewBox (todas las capas se vectorizan sin recortar, mismo sistema de coordenadas — ya verificado en M2-S02). `LayerComponent`/`ComponentAnalysisService` (M2-S03/MVP2) ya calcula `componentCount` por capa (vía análisis de componentes conexos). `ManufacturingOperation` (M2-S07/MVP2) ya persiste corte/grabado/ignorar por capa. Lo que NO existe hoy: un endpoint/contrato único que combine todo esto en la forma `VectorLayer` pedida por esta tarjeta, ni persistencia de `visible`/`locked`/`order` (hoy `visible` es estado efímero de React, `locked` no existe, `order` es implícito por posición en la lista de la paleta), ni la validación raster-vs-vector.

## Criterios de aceptación ampliados

- [ ] **Validación raster-vs-vector (lo más nuevo/valioso de esta tarjeta)**: después de generar el SVG de una capa, rasterizarlo de vuelta (mismo tamaño que la máscara original) y compararlo contra la máscara raster de origen de esa capa (de M2-S01), con una tolerancia explícita (relativa, ej. % de píxeles discrepantes) — documentar el umbral elegido y por qué. Crucial: la comparación debe detectar si aparecen píxeles de la máscara de OTRO color dentro del área vectorizada de la capa actual (contaminación cruzada) — no solo "cuántos píxeles coinciden en general". Si la discrepancia supera la tolerancia, reportarlo (log/warning; decidir si debe bloquear la generación o solo advertir, documentar la decisión).
- [ ] **Modelo `VectorLayer` consolidado**: un endpoint/contrato que combine, para cada capa: `id` (GroupId), `name`, `colorId`/`colorHex`, `fill` (ya existe desde M2.1-S01), `paths[]` o referencia al SVG, `componentCount` (ya calculado por M2-S03/MVP2, combinarlo acá en vez de requerir una llamada aparte), `manufacturingOperation` (ya persistido por M2-S07/MVP2, combinarlo acá). Para `visible`/`locked`/`order`: ver Ambigüedades — la persistencia INTERACTIVA de estos tres campos específicamente es responsabilidad explícita de M2.1-S07 ("Persists order, name, visible, locked, and operation"), así que esta tarjeta expone su VALOR ACTUAL/default en el contrato (ej. `visible=true`, `locked=false`, `order=` posición en la paleta) sin necesariamente construir endpoints de mutación nuevos para ellos — el implementador puede adelantar la persistencia si le resulta natural, pero no es obligatorio para esta tarjeta.
- [ ] **Reordenar no debe alterar geometría**: dado que `order` hoy es implícito (posición en `palette.Groups`), y la geometría de cada capa vive en su propia `VectorVersion` (independiente del orden), esto ya debería cumplirse por diseño — agregar un test explícito que lo confirme (cambiar el orden reportado/persistido, si se implementa, y verificar que ningún `VectorId`/SVG cambia).
- [ ] **IDs persisten**: ya debería cumplirse (`GroupId`/`VectorId` estables) — test explícito si no existe uno ya cubriendo este caso específico para el contrato consolidado.
- [ ] **Tests**: 4 colores (ya cubierto en parte, reverificar con el nuevo contrato), huecos (topología con agujeros, ya cubierto por M2-S02/M2-S03), componentes separados del mismo color (ya cubierto por M2-S03/componentes), transparencia (ya cubierto), colores contiguos (dos colores que se tocan — verificar que la validación raster-vs-vector NO reporta falsos positivos de "contaminación" en el borde de contacto, dado el antialiasing/redondeo de coordenadas).
- [ ] Fuera de alcance: NO construir herramientas manuales del editor (drag-and-drop, etc. — eso es M2.1-S06/S07).

## Referencias visuales / de marca
MISSING — no hay links ni adjuntos en la tarjeta.

## Umbrales de calidad
No aplican Lighthouse/axe. Estándar ya usado: build sin errores/warnings nuevos, los 3 test suites completos en verde.

## Ambigüedades detectadas
- **Solape con M2.1-S07** sobre `visible`/`locked`/`order`: S07 dice explícitamente "Persists order, name, visible, locked, and operation" como parte de SU alcance. Recomendación del orquestador (no vinculante): S03 expone estos 3 campos en el contrato `VectorLayer` con valores default/computados razonables (no requiere mutación persistente todavía), S07 construye la interactividad + persistencia real más adelante. Si el implementador prefiere adelantar la persistencia acá porque es más simple de una vez, está bien — documentar la decisión para que S07 la reutilice en vez de duplicarla.
- **Umbral de tolerancia de la validación raster-vs-vector**: no cuantificado por el spec — el implementador decide un valor razonable (ej. relativo al área total, consistente con el estilo de tolerancias ya usado en M2-S03/componentes) y lo documenta con evidencia.
- **Bloquear vs. advertir** si la validación raster-vs-vector falla: no especificado — recomendación: advertir/loggear por ahora (no bloquear la generación de la capa), salvo que el implementador encuentre evidencia de que debería ser bloqueante; documentar la decisión.
- Ninguna otra ambigüedad bloqueante.

## Nota de orquestación
Tercera tarjeta de MVP 2.1, corrida en modo autónomo (mismo criterio ya establecido).
