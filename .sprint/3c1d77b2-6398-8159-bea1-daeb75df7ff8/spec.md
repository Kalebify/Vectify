# M2-S05 · Agrupar componentes
URL: https://app.notion.com/p/3c1d77b263988159bea1daeb75df7ff8

Quinta tarjeta de MVP2. Introduce agrupación LÓGICA de componentes (M2-S03) — tratar varias piezas como un conjunto para operarlas juntas (mover/seleccionar) — SIN modificar geometría ni fusionar piezas físicamente. Explícitamente distinto de "unión física" (M2-S06, posterior).

Stack: React (multi-select, árbol) + ASP.NET Core (persistir grupos, validar referencias).

## Requerimiento (transcripción del cuerpo de la tarjeta)

### Contexto
Agrupar no significa unir físicamente. Este sprint introduce agrupación lógica para operar varias piezas juntas sin tocar sus paths.

### Usuario podrá
Seleccionar componentes, Agrupar, renombrar grupo, mover/seleccionar como conjunto cuando el editor lo permita y Desagrupar.

### Modelo
`ComponentGroup` referencia IDs de componentes; no crea una unión geométrica ni cambia el número de piezas físicas.

### React
Multi-select, acciones group/ungroup y representación en árbol.

### ASP.NET Core
Persistir grupos y validar que referencias pertenezcan a versión compatible.

### Pruebas
Agrupar/desagrupar conserva hash/paths de geometría; grupos múltiples y eliminación de componente.

### Definition of Done
Agrupar es reversible y conserva exactamente la geometría original.

### Fuera de alcance
Boolean union/bridges (eso es M2-S06).

## Criterios de aceptación

De la propiedad "Criterio de aceptación": "Agrupar/desagrupar conserva exactamente los paths originales."

Ampliados por el cuerpo de la tarjeta:
- [ ] Multi-select de componentes (de M2-S03, dentro de una o más capas) en el árbol Layer→Components ya existente.
- [ ] Acción "Agrupar": crea un `ComponentGroup` que referencia los IDs de los componentes seleccionados (por `ComponentSetVersion`/`VectorId` + `componentId`, consistente con cómo M2-S03 ya identifica un componente). NO modifica ningún path, NO crea una nueva `VectorVersion`/`ComponentSetVersion`, NO cambia la cantidad de piezas físicas — es una referencia lógica pura.
- [ ] Acción "Desagrupar": elimina/invalida el `ComponentGroup` (o lo marca deshecho); los componentes vuelven a existir individualmente exactamente como antes de agrupar — sin ninguna pérdida ni aproximación de geometría (criterio de aceptación explícito: "conserva exactamente los paths").
- [ ] Renombrar grupo: el usuario puede darle un nombre al `ComponentGroup`.
- [ ] "Mover/seleccionar como conjunto cuando el editor lo permita": dado que este proyecto NO tiene un editor de manipulación directa de geometría (no hay drag-and-drop de piezas en el canvas en ningún sprint anterior ni en el alcance declarado de MVP2), el implementador debe interpretar esto como: seleccionar el grupo en el árbol selecciona/resalta TODOS sus componentes miembro a la vez en el canvas (extensión de la selección bidireccional ya existente de M2-S03/M2-S04), sin necesidad de implementar un editor de arrastre nuevo — documentar esta interpretación explícitamente.
- [ ] Backend .NET: nuevo modelo `ComponentGroup` (referencia una lista de `componentId`s + su `ComponentSetVersion`/`VectorId` de origen), persistido siguiendo el mismo patrón de versionado inmutable que el resto del pipeline (agrupar/desagrupar/renombrar crean una versión nueva del conjunto de grupos, nunca mutan una existente). Validación: todo `componentId` referenciado debe existir en la versión de componentes indicada — si la versión de componentes cambia (se recalculó M2-S03), un `ComponentGroup` viejo que referencia IDs de una versión anterior debe seguir siendo válido para ESA versión vieja (los componentes de M2-S03 son inmutables una vez calculados), pero no debe poder crearse un grupo nuevo mezclando IDs de dos versiones distintas.
- [ ] Tests: agrupar y desagrupar deben conservar EXACTAMENTE el hash/contenido de los paths de geometría subyacente (test explícito que compara bytes/hash antes y después del ciclo agrupar→desagrupar); grupos múltiples simultáneos (más de un `ComponentGroup` coexistiendo, sin solaparse necesariamente — decidir y documentar si un componente puede pertenecer a más de un grupo a la vez, dado que el spec no lo prohíbe explícitamente pero tampoco lo pide); eliminación de un componente referenciado por un grupo (si M2-S03 recalcula y ese componente ya no existe en la nueva versión, el grupo viejo sigue siendo válido para la versión vieja, pero debe manejarse sin crashear si se consulta contra la versión nueva).
- [ ] Fuera de alcance: NO implementar boolean union ni bridges (M2-S06). Agrupar nunca reduce la cantidad de piezas físicas reales.

## Referencias visuales / de marca
MISSING — no hay links ni adjuntos en la tarjeta.

## Umbrales de calidad
No aplican Lighthouse/axe. Se mantiene el estándar ya usado: `dotnet build` sin errores/warnings nuevos, los test suites relevantes en verde.

## Ambigüedades detectadas
- **"Mover/seleccionar como conjunto cuando el editor lo permita"**: ver criterio de aceptación de arriba — interpretado como selección conjunta en el árbol/canvas, NO como un editor de arrastre nuevo (no existe tal editor en el proyecto). El implementador puede apartarse si encuentra una interpretación mejor, pero debe documentarla.
- **¿Un componente puede pertenecer a más de un grupo?**: no especificado. El implementador decide (recomendación: permitirlo, ya que "referencia IDs" sin más restricción no lo prohíbe, y restringirlo agregaría una validación no pedida) y lo documenta.
- **Compatibilidad entre versiones de `ComponentSetVersion`**: no especificado en detalle. El implementador decide el criterio exacto de "versión compatible" (recomendación: un grupo se valida contra la versión de componentes que existía en el momento de crearlo, identificada por su ID; no se intenta "migrar" un grupo automáticamente a una versión de componentes recalculada distinta) y lo documenta.
- Ninguna otra ambigüedad bloqueante.

## Nota de orquestación
Corrida en modo autónomo (M2-S01..M2-S07 sin confirmación por paso, incluido merge de PRs), por autorización explícita del usuario. Tarjetas que no puedan cerrarse quedan en "Bloqueada" en Notion con el motivo documentado.
