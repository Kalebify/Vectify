# M2-S03 · Componentes independientes por capa
URL: https://app.notion.com/p/3c1d77b2639881c0ac56ebe2671ae7a3

Tercera tarjeta de MVP2. Consume una capa vectorial (`VectorLayer`/`VectorVersion`) generada por M2-S02 y distingue, DENTRO de esa capa de un solo color, cuántas piezas físicas geométricamente separadas contiene ("Azul: 3 piezas"). No modifica geometría, no fusiona nada — solo detecta y expone la estructura ya existente.

Stack: a diferencia de M2-S01/M2-S02, la propia tarjeta NO tiene secciones separadas "ASP.NET Core"/"Python" — Área declarada es "Vector, Laser, Frontend" (sin "ASP.NET Core" explícito). Esto es una decisión de implementación abierta (ver Ambigüedades).

## Requerimiento (transcripción del cuerpo de la tarjeta)

### Contexto
Una capa de un color puede contener varias piezas físicas separadas. Debemos distinguir capa lógica de componente geométrico.

### Usuario podrá
Ver "Azul: 3 piezas", seleccionar cada componente y localizarlo en canvas.

### Vector/Laser
Calcular connected components sobre geometría de cada layer; asignar IDs estables dentro de versión; bounds, área y relaciones de contención cuando proceda.

### React
Árbol Layer → Components, selección bidireccional lista/canvas y métricas.

### Pruebas
Islas separadas, agujeros internos, piezas tocándose, tolerancias y componentes diminutos.

### Definition of Done
Cada layer informa de sus componentes físicos de forma consistente y seleccionable.

### Fuera de alcance
Modificar la geometría para unirlos (eso sería M2-S06, unión física — explícitamente distinto de "componente" acá).

## Criterios de aceptación

De la propiedad "Criterio de aceptación": "Cada capa informa y visualiza sus componentes geométricos separados."

Ampliados por el cuerpo de la tarjeta:
- [ ] Cálculo de "connected components" sobre la geometría SVG de cada `VectorLayer` existente (subpaths de un mismo `<path>`, o múltiples `<path>` — depende de cómo VTracer/M1-S05 estructure el SVG resultante): un componente es un conjunto de subpaths que forman una pieza física conexa; un subpath que es un AGUJERO dentro de otro (contenido geométricamente, no separado) pertenece al MISMO componente que lo contiene, no es un componente aparte (ver "relaciones de contención cuando proceda").
- [ ] IDs estables DENTRO de una versión: mientras la `VectorLayerSetVersion`/`VectorVersion` de origen no cambie, los IDs de componente deben ser reproducibles (mismo layer + mismos parámetros → mismos IDs, mismo orden). No es necesario que sean estables ENTRE versiones distintas (una nueva versión puede reasignar IDs).
- [ ] Por cada componente: bounds (bounding box), área aproximada, y su relación de contención si aplica (qué componente/agujero contiene a qué otro, cuando sea relevante para reportarlo).
- [ ] Backend: persistir el resultado del análisis de componentes por capa (versionado, consistente con el resto del pipeline — no recalcular en cada request de lectura si ya se calculó para esa combinación layer+versión). El implementador decide si el cálculo geométrico en sí corre en Python (reutilizando `svg_path_parsing.py`/el tokenizer ya compartido con el Laser Checker de M1-S08, dado que ya resuelve el parseo de subpaths/transform) o directamente en C# — ver Ambigüedades.
- [ ] Frontend: árbol "Layer → Components" (cada layer expande a sus componentes), selección bidireccional (click en la lista resalta el componente en el canvas y viceversa), y métricas visibles (cantidad de piezas por capa, ej. "Azul: 3 piezas", bounds/área por componente seleccionado).
- [ ] Tests: islas separadas (2+ formas sin ningún punto en común), agujeros internos (un componente con un subpath interior que es un hueco, no un componente aparte), piezas tocándose (dos formas que comparten un punto/borde — el implementador decide y documenta el criterio exacto de "tocarse" que las separa o las une, con una tolerancia explícita, consistente con el estilo de tolerancias relativas ya usado en M1-S08), tolerancias (mismo criterio), y componentes diminutos (un componente de área casi nula — decidir y documentar si se filtra como ruido o se reporta igual).
- [ ] NO modificar geometría para unir piezas — esta tarjeta solo INFORMA la estructura existente, no la cambia (eso es M2-S06, "unión física de piezas", una operación explícitamente distinta y posterior).

## Referencias visuales / de marca
MISSING — no hay links ni adjuntos en la tarjeta.

## Umbrales de calidad
No aplican Lighthouse/axe. Se mantiene el estándar ya usado: build sin errores/warnings nuevos, los test suites relevantes en verde antes de dar la tarjeta por terminada.

## Ambigüedades detectadas
- **Split de stack no explícito** (la más importante de esta tarjeta): a diferencia de M2-S01/M2-S02, el cuerpo de la tarjeta no tiene una sección "ASP.NET Core" separada, y "Área" no incluye "ASP.NET Core". El implementador debe decidir dónde vive el cálculo geométrico. Recomendación del orquestador (no vinculante, el implementador puede apartarse si tiene una razón mejor y la documenta): dado que M1-S08 (Laser Checker) ya estableció el precedente de análisis geométrico de paths SVG en Python (`path_checker.py` + `svg_path_parsing.py` compartido, con manejo de `transform`, subpaths, tolerancias relativas a la diagonal del bounding box), lo más consistente es reutilizar ese mismo tokenizer/infraestructura en Python para el análisis de connected components, con un endpoint Python nuevo, y una capa fina en ASP.NET Core (aunque no esté explícita en la tarjeta) que persista el resultado versionado y lo exponga al frontend — siguiendo el mismo patrón arquitectónico de TODAS las tarjetas anteriores (Python analiza, .NET persiste/orquesta/expone, React consume). NO se considera aceptable hacer el cálculo geométrico en el frontend (JS) ya que rompería el patrón "el navegador no es la fuente de verdad" usado en todo el proyecto.
- Criterio exacto de "piezas tocándose" (¿un punto de contacto las une o las separa como componentes distintos?): no especificado. El implementador decide (probablemente: comparten al menos un punto exacto o dentro de tolerancia → mismo componente físico, ya que geométricamente serían una sola pieza al cortar) y lo documenta con justificación.
- Umbral de "componente diminuto" no cuantificado — el implementador elige un valor razonable y lo documenta (ej. relativo al área total de la capa o a la diagonal del bounding box, consistente con el estilo ya usado).
- Ninguna otra ambigüedad bloqueante.

## Nota de orquestación
Corrida en modo autónomo (M2-S01..M2-S07 sin confirmación por paso, incluido merge de PRs), por autorización explícita del usuario. Tarjetas que no puedan cerrarse quedan en "Bloqueada" en Notion con el motivo documentado.
