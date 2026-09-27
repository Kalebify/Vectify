# M2.1-S02 · Detección y reducción real de paleta
URL: https://app.notion.com/p/3e8d77b26398819b9119ebc2e252e3bf

Segunda tarjeta de MVP 2.1. A diferencia de una feature nueva desde cero, esta tarjeta ENDURECE y COMPLETA la detección de paleta ya construida en M2-S01 (MVP2, `ColorPaletteService`/`color_palette_pipeline.py`, ya mergeada y funcionando): agrega el flag incluido/excluido por color, ataca explícitamente la explosión de grupos por antialiasing (ya detectada como limitación conocida en la auditoría M2.1-S01), y cubre casos límite con tests deterministas. El cuerpo de la tarjeta permite EXPLÍCITAMENTE mantener el algoritmo de clustering ya existente ("u opción ya existente") — no exige una reescritura a k-means/median-cut si lo actual ya cumple.

Stack: Python (cuantización/tolerancia) + Frontend (panel de paleta, ya existe, se extiende) + Vector.

## Requerimiento (transcripción del cuerpo de la tarjeta)

### Objetivo (propiedad Notion)
Convertir una imagen multicolor en una paleta estable y configurable que represente sus colores visualmente relevantes.

### Criterio de aceptación (propiedad Notion)
El usuario ve la paleta detectada, puede cambiar el número objetivo de colores y confirmar/combinar colores sin destruir el original.

### Resultado esperado
Una imagen multicolor debe producir una paleta real, no un resultado binario. Cada entrada tendrá id estable, color RGB/HEX, porcentaje aproximado de cobertura y estado incluido/excluido.

### Python
Implementar/ajustar cuantización con estrategia intercambiable. Evaluar k-means/median-cut u opción ya existente. Tratar transparencia y permitir excluir fondo. Evitar crear decenas de colores por antialiasing.

### Frontend
Panel de paleta con swatch, HEX, cobertura, incluir/excluir, merge y número objetivo de colores. Preview antes de confirmar.

### Contrato sugerido
`PaletteResult: paletteId, sourceVersionId, colors[{id, hex, rgb, coverage, isBackground}], algorithm, parameters` (sugerido, no obligatorio literal — ver Ambigüedades).

### Casos límite
Gradientes, colores muy próximos, transparencia, fondo dominante, imagen B/N y paleta solicitada mayor que colores reales.

### Tests
Fixtures deterministas y tolerancias explícitas. Verificar estabilidad de IDs durante la edición de la paleta.

### Definition of Done
La paleta confirmada puede alimentar S03 sin volver a analizar arbitrariamente la imagen.

### Fuera de alcance
Edición manual de paths y generación IA.

## Estado actual (ya construido en M2-S01/MVP2 — punto de partida, no reescribir desde cero)

Ya existe y funciona: `ColorPaletteService` (.NET, versionado inmutable cache+lock), `color_palette_pipeline.py` (clustering determinista en espacio Lab, tolerancia + `max_colors` configurables, exclusión de píxeles totalmente transparentes, `has_partial_alpha` por grupo), endpoints detect/merge/unmerge/rename/confirm, panel de React con swatches/hex/%área/merge/unmerge/confirm. La auditoría M2.1-S01 ya documentó (no corrigió, estaba fuera de su alcance) que con antialiasing el clustering genera MUCHOS grupos extra de "colores casi iguales" en bordes suavizados (17 grupos en vez de ~5 lógicos en el caso documentado).

## Criterios de aceptación ampliados (trabajo real de esta tarjeta)

- [ ] **Flag incluido/excluido por color** (NUEVO, no existe hoy): cada `ColorGroup` gana un estado `IsExcluded`/`IsBackground` (booleano), distinto de "merge" — un color puede marcarse como excluido SIN fusionarlo a otro grupo. Un grupo excluido no debe ser tenido en cuenta en M2-S03 (generación de capas) cuando esa tarjeta lea la paleta confirmada — para ESTA tarjeta alcanza con persistir y exponer el flag correctamente (versionado, como el resto de ediciones de paleta), la exclusión efectiva aguas abajo es responsabilidad de S03.
- [ ] **Detección de fondo dominante**: el grupo con mayor `AreaPercent` que además toque los bordes de la imagen (heurística razonable, a definir/documentar por el implementador) puede pre-marcarse como `IsBackground=true` por default, pero el usuario SIEMPRE puede cambiarlo manualmente (incluir/excluir es una acción explícita del usuario, la detección automática es solo una sugerencia inicial, no una regla rígida).
- [ ] **Atacar la explosión de colores por antialiasing** (causa raíz ya documentada en M2.1-S01: bordes suavizados generan grupos minúsculos de "colores casi iguales" que además a veces ni siquiera producen un `<path>` vectorizable): ajustar la tolerancia por defecto y/o agregar un umbral de "área mínima de grupo" (relativo al total, mismo estilo que `tiny_area_ratio` ya usado en M2-S03/componentes) para fusionar automáticamente grupos minúsculos hacia su vecino de color más cercano ANTES de devolver la paleta final — el usuario no debería tener que lidiar manualmente con 17 grupos cuando la imagen tiene 5 colores lógicos. Documentar el umbral elegido y su justificación. Esto debe ser opcional/configurable (no debe impedir que un usuario avanzado pida explícitamente muchos colores finos si lo desea vía `maxColors`/tolerancia).
- [ ] **Casos límite con tests deterministas explícitos**:
  - Gradientes (transición continua de color): confirmar comportamiento razonable (no explota en cientos de grupos gracias al fix de arriba, y no colapsa todo a 1 color salvo que la tolerancia lo pida).
  - Colores muy próximos (ya cubierto en parte por M2-S01, "colores casi iguales" — reverificar).
  - Transparencia (ya cubierto, reverificar que sigue funcionando tras cualquier cambio).
  - Fondo dominante (ver detección automática arriba).
  - Imagen B/N (2 colores): debe seguir funcionando exactamente igual que hoy (regresión).
  - Paleta solicitada (`maxColors`) mayor que la cantidad de colores reales: no debe fallar ni inventar colores — debe devolver la cantidad real (menor a la pedida) sin error.
- [ ] **Estabilidad de IDs durante la edición**: confirmar (con test explícito) que el `GroupId` de un grupo NO fusionado se mantiene igual entre ediciones sucesivas de la misma sesión de paleta (rename, exclude/include no deberían generar un GroupId nuevo para grupos no afectados) — ya debería cumplirse por el diseño actual (`SortGroups`/versionado preserva grupos no tocados), pero hay que verificarlo con un test dedicado, no asumirlo.
- [ ] **Contrato**: no es obligatorio adoptar el nombre literal `PaletteResult`/`colors[]` si ya existe un contrato equivalente funcionando (`ColorPaletteResponse`/`groups[]`) — evaluar si conviene exponer también `rgb` (además de `hex`, ya existente) dado que el contrato sugerido lo pide explícitamente; si se agrega, que sea aditivo (no romper el contrato ya consumido por el frontend de M2-S01/M2-S02/M2-S05/M2.1-S01).
- [ ] **Frontend**: agregar el toggle incluir/excluir por swatch en el panel ya existente (no crear un panel paralelo) — reutilizando `ColorPalettePanel.tsx`/`ColorSwatchList.tsx` de M2-S01.
- [ ] Fuera de alcance: NO implementar edición manual de paths, NO generación por IA.

## Referencias visuales / de marca
MISSING — no hay links ni adjuntos en la tarjeta.

## Umbrales de calidad
No aplican Lighthouse/axe. Estándar ya usado: build sin errores/warnings nuevos, los 3 test suites completos en verde (no solo los tests nuevos).

## Ambigüedades detectadas
- **Algoritmo**: el cuerpo permite explícitamente mantener "la opción ya existente" (clustering Lab determinista) — no es necesario evaluar/migrar a k-means/median-cut salvo que el implementador encuentre una razón concreta para hacerlo. Documentar la decisión de mantener o cambiar, con justificación.
- **Heurística de fondo dominante**: no especificada en detalle — el implementador decide un criterio razonable (ej. mayor área + toca el borde de la imagen en un % alto de su perímetro) y lo documenta. Es una PRE-marca sugerida, nunca obligatoria — el usuario manda.
- **Umbral de "grupo minúsculo" para el fix de antialiasing**: no cuantificado — el implementador elige un valor razonable (relativo al área total, consistente con `tiny_area_ratio` de M2-S03) y lo documenta con evidencia (antes/después, cuántos grupos, en el mismo fixture con antialiasing que ya generó la auditoría M2.1-S01 si es reutilizable).
- Ninguna otra ambigüedad bloqueante.

## Nota de orquestación
Segunda tarjeta de MVP 2.1, corrida en modo autónomo (mismo criterio ya establecido para MVP2 y M2.1-S01: sin pedir confirmación por paso, incluido merge de PRs).
