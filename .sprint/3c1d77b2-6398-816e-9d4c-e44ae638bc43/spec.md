# M2-S04 · Vista explotada por colores
URL: https://app.notion.com/p/3c1d77b26398816e9d4ce44ae638bc43

Cuarta tarjeta de MVP2. Tarjeta MAYORMENTE FRONTEND: hace comprensible el diseño multicapa (capas de M2-S02, componentes de M2-S03) antes de fabricar, mostrando una vista "explotada" (capas separadas visualmente) además de la vista ensamblada normal. Explícitamente NO persiste geometría nueva — es una vista, no una transformación de datos.

Stack: React (vista explotada) + un backend que solo entrega metadata YA EXISTENTE (no calcula nada nuevo, no genera versiones nuevas).

## Requerimiento (transcripción del cuerpo de la tarjeta)

### Contexto
Hacer comprensible el diseño multicapa antes de fabricar.

### Usuario podrá
Alternar vista ensamblada/explotada, aislar colores, controlar separación visual y volver al origen sin alterar geometría.

### React
Vista exploded puramente visual; layers desplazadas para inspección, leyenda y contador de piezas por color.

### Backend
Entregar metadata necesaria; no persistir desplazamientos visuales como geometría real.

### Pruebas
2–10 capas, muchos componentes, responsive y selección consistente entre vistas.

### Definition of Done
El usuario entiende qué pieza corresponde a cada color sin riesgo de modificar accidentalmente coordenadas de fabricación.

### Fuera de alcance
Nesting y animación decorativa.

## Criterios de aceptación

De la propiedad "Criterio de aceptación": "Usuario puede aislar/ocultar cada color y reconocer claramente las piezas resultantes."

Ampliados por el cuerpo de la tarjeta:
- [ ] Toggle explícito entre vista "ensamblada" (todas las capas superpuestas en su posición real, igual que el panel Layers de M2-S02) y vista "explotada" (capas desplazadas visualmente para poder inspeccionarlas por separado).
- [ ] El desplazamiento en la vista explotada es PURAMENTE VISUAL: se calcula y aplica del lado del cliente (transform CSS/SVG, no coordenadas reales) — NO se envía al backend, NO se persiste como una nueva versión de nada, NO modifica ningún `d` de path. Volver a la vista ensamblada debe recuperar exactamente la posición original sin ninguna pérdida/aproximación.
- [ ] Control de separación visual: el usuario puede ajustar cuánto se separan las capas en la vista explotada (ej. un slider), sin límite superior estricto salvo el razonable para que siga siendo usable.
- [ ] Aislar color: en cualquiera de las dos vistas, el usuario puede aislar (ver solo) o mostrar/ocultar cualquier color individual (reutilizando el toggle de visibilidad ya existente del panel Layers de M2-S02, no duplicar esa lógica).
- [ ] Leyenda: lista de colores/capas visible en la vista explotada, con nombre y color de cada una.
- [ ] Contador de piezas por color: usa el resultado YA CALCULADO de M2-S03 (componentes por capa) para mostrar cuántas piezas físicas tiene cada color (ej. "Azul: 3 piezas") — no recalcular, solo consumir el dato existente.
- [ ] Backend: si hace falta un endpoint/campo adicional para "entregar la metadata necesaria" (ej. exponer de forma más conveniente el conteo de componentes por capa junto con la lista de capas, en una sola respuesta), el implementador decide si ya alcanza con los endpoints existentes (M2-S02 lista de capas + M2-S03 componentes por capa, combinados del lado del cliente) o si conviene un endpoint de agregación — pero en NINGÚN caso el backend debe persistir un desplazamiento visual ni crear una versión nueva de nada por esta tarjeta.
- [ ] Selección consistente entre vistas: si el usuario selecciona un componente/capa en la vista ensamblada y cambia a la vista explotada (o viceversa), la selección se mantiene.
- [ ] Responsive: debe funcionar razonablemente en distintos anchos de viewport (no se espera un layout específico de mobile-first, pero no debe romperse en pantallas angostas).
- [ ] Tests: 2–10 capas (rango representativo, no solo 1 o un número fijo), muchos componentes (una capa con varios componentes de M2-S03), responsive (al menos un test de layout en un viewport angosto), selección consistente entre vistas (test explícito del caso descripto arriba).
- [ ] Fuera de alcance: nesting (organizar piezas para aprovechar espacio de corte — eso sería otra tarjeta/MVP) y animación decorativa (transiciones puramente estéticas no requeridas, aunque una transición simple no está prohibida si no es el foco del trabajo).

## Referencias visuales / de marca
MISSING — no hay links ni adjuntos en la tarjeta.

## Umbrales de calidad
No aplican Lighthouse/axe formales, pero dado que esta tarjeta es explícitamente de UI/UX ("hacer comprensible el diseño"), prestar atención razonable a claridad visual y responsive (ver Pruebas). Se mantiene el estándar ya usado: build sin errores/warnings nuevos, tests en verde.

## Ambigüedades detectadas
- No se especifica el algoritmo exacto de desplazamiento visual (ej. desplazar en línea recta según un ángulo, en abanico, en grilla) — el implementador decide un criterio simple y predecible (ej. desplazamiento radial o lineal proporcional al índice de la capa) y lo documenta.
- "Backend: entregar metadata necesaria" es deliberadamente vago — el implementador decide si hace falta algún endpoint nuevo o alcanza con combinar los datos ya expuestos por M2-S02/M2-S03 del lado del cliente; dado el principio YAGNI ya aplicado en tarjetas anteriores (Checking M1-S08, Export M1-S10 sin cache/registro porque no generaban artefactos nuevos), la opción por defecto razonable es NO agregar backend nuevo si los datos ya están disponibles combinables en el cliente.
- Ninguna otra ambigüedad bloqueante.

## Nota de orquestación
Corrida en modo autónomo (M2-S01..M2-S07 sin confirmación por paso, incluido merge de PRs), por autorización explícita del usuario. Tarjetas que no puedan cerrarse quedan en "Bloqueada" en Notion con el motivo documentado.
