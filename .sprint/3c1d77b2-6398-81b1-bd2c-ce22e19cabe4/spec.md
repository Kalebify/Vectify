# M2-S06 · Unión física de piezas
URL: https://app.notion.com/p/3c1d77b2639881b1bd2cce22e19cabe4

Sexta tarjeta de MVP2. A diferencia de M2-S05 (agrupar, lógico, sin tocar geometría), esta tarjeta SÍ MODIFICA geometría: fusiona componentes seleccionados (M2-S03) en una única pieza físicamente fabricable, usando operaciones booleanas y/o "bridging" (puentes de conexión) según la relación geométrica entre las piezas. Es la primera tarjeta de MVP2 que requiere una librería de geometría booleana nueva (ninguna dependencia actual del proyecto la provee).

Stack: Python/Vector Engine (operaciones booleanas/bridging) + ASP.NET Core (comando versionado, nueva `VectorVersion`, historial) + React (preview antes/después, advertencias).

## Requerimiento (transcripción del cuerpo de la tarjeta)

### Contexto
A diferencia de Agrupar, aquí sí se modifica geometría para intentar obtener una sola pieza fabricable.

### Usuario podrá
Seleccionar piezas, solicitar Unión física, ver preview antes/después, número de componentes resultante y confirmar/cancelar.

### Vector Engine
Usar operaciones booleanas/bridging según geometría; Clipper2 es candidato para booleanas/offsets. Nunca fingir unión si las piezas siguen desconectadas.

### ASP.NET Core
Comando versionado, nueva VectorVersion, historial y validación post-operación.

### React
Explicar claramente `Agrupar` vs `Unir físicamente`; preview y advertencias.

### Pruebas
Polígonos solapados, tangentes, separados, agujeros y geometría inválida.

### Definition of Done
Cuando es geométricamente posible, el resultado es un componente válido; si no, la UI explica por qué y no destruye la versión previa.

### Fuera de alcance
Generación inteligente de bridges complejos (MVP3).

## Criterios de aceptación

De la propiedad "Criterio de aceptación": "La unión genera geometría válida y el analizador la reconoce como un único componente cuando sea geométricamente posible."

Ampliados por el cuerpo de la tarjeta:
- [ ] Selección de 2+ componentes (de M2-S03, dentro de una misma capa/`VectorId`) y acción "Unión física" (distinta y explícitamente etiquetada frente a "Agrupar" de M2-S05 en toda la UI).
- [ ] Estrategia geométrica según relación espacial entre las piezas seleccionadas:
  - **Solapadas o tangentes** (se tocan o se superponen): unión booleana estándar (union de polígonos) — el resultado es geométricamente una sola forma conexa de verdad, no una aproximación.
  - **Separadas** (no se tocan): requiere un "bridge" (puente) — geometría adicional mínima que conecta físicamente las piezas (ej. un rectángulo/segmento engrosado entre los puntos más cercanos de cada pieza) para que el resultado sea una sola forma conexa real. Ver "Fuera de alcance": el bridge puede ser simple/directo (conexión recta entre puntos más cercanos), NO se espera un algoritmo inteligente de posicionamiento/optimización de bridges (eso es MVP3).
  - Librería candidata para las operaciones booleanas/offset: **Clipper2** (mencionada explícitamente en el cuerpo de la tarjeta). El implementador puede evaluar alternativas (ej. Shapely/GEOS, con más ecosistema y funciones de buffer/dilate útiles para bridging) si Clipper2 no tiene bindings Python estables/mantenidos en este momento — decisión técnica a documentar con justificación en IMPL.md. Es la primera tarjeta del proyecto que agrega una dependencia de geometría booleana nueva a `requirements.txt`; está explícitamente sancionado por el propio spec ("Clipper2 es candidato"), no es una desviación.
- [ ] **Nunca fingir unión si las piezas siguen desconectadas**: after generar el resultado, se debe VERIFICAR realmente que es una sola pieza conexa — reutilizar el mismo análisis de connected components de M2-S03 (`component_analysis.py`/`collect_document_subpaths`) sobre el SVG resultante: si tras la operación el análisis sigue detectando más de un componente, la unión FALLÓ (no se debe persistir como si hubiera funcionado) — devolver un error explícito indicando que no fue geométricamente posible, con el motivo si se puede determinar.
- [ ] Preview antes/después: antes de confirmar, el usuario ve una vista previa del resultado de la unión (geometría real calculada, no una aproximación visual) y el número de componentes resultante (debería ser 1 si fue exitosa).
- [ ] Confirmar/cancelar: la operación no se persiste hasta que el usuario confirma explícitamente; cancelar no genera ningún cambio.
- [ ] Backend .NET: comando versionado que, al confirmar, crea una NUEVA `VectorVersion` (reutilizando el tipo existente de M1-S05/M2-S02, no un tipo paralelo) con el SVG modificado — la capa de origen pasa a tener una nueva versión en su historial; la versión anterior NO se destruye (sigue existiendo en el historial, recuperable). Validación post-operación: antes de persistir, confirmar que el resultado es geometría SVG válida (parseable) y que el análisis de componentes lo reconoce como 1 solo componente (salvo que la unión involucrara piezas que no debían fusionarse entre sí — ver Ambigüedades sobre selección parcial).
- [ ] Si la unión NO es geométricamente posible (ej. combinación de geometría inválida que ni booleana ni bridging puede resolver de forma segura): la UI debe explicar POR QUÉ (mensaje claro, no un error genérico), y la versión previa de la capa NO debe modificarse ni destruirse — el usuario sigue teniendo exactamente lo que tenía antes de intentar la unión.
- [ ] Frontend: distinguir claramente "Agrupar" (M2-S05, lógico, no toca geometría) de "Unir físicamente" (M2-S06, modifica geometría) en toda la UI — textos, iconos o ubicación que dejen claro que son operaciones DISTINTAS con consecuencias distintas (una es reversible sin pérdida, la otra genera una nueva versión con geometría realmente distinta). Preview antes/después y advertencias visibles antes de confirmar.
- [ ] Tests: polígonos solapados (unión booleana estándar), tangentes (se tocan en un punto/borde, unión booleana o bridging mínimo según corresponda), separados (requieren bridging real), agujeros (una de las piezas o el resultado tiene topología con huecos — la unión debe preservarlos correctamente, no "rellenarlos" por error), geometría inválida (polígono autointersectante o degenerado que no puede procesarse de forma segura — debe fallar con un mensaje claro, no producir un resultado corrupto silencioso).
- [ ] Fuera de alcance: NO implementar generación inteligente de bridges complejos (posicionamiento óptimo considerando múltiples piezas, ancho variable, resistencia estructural, etc.) — eso es MVP3. Un bridge simple/directo para el caso "piezas separadas" SÍ está dentro del alcance mínimo necesario para que la operación pueda tener éxito en ese caso (ver arriba).

## Referencias visuales / de marca
MISSING — no hay links ni adjuntos en la tarjeta.

## Umbrales de calidad
No aplican Lighthouse/axe. Se mantiene el estándar ya usado: `dotnet build` sin errores/warnings nuevos, los test suites relevantes en verde. Dado que esta tarjeta modifica geometría real por primera vez en MVP2, prestar especial atención a la corrección geométrica (validación post-operación no es opcional, es un criterio de aceptación explícito).

## Ambigüedades detectadas
- **Librería de geometría booleana**: el spec nombra Clipper2 como candidato pero no lo exige de forma estricta ("candidato"). El implementador decide y documenta, evaluando bindings Python disponibles y mantenidos (Clipper2 vía `pyclipper2`/similar, o Shapely/GEOS como alternativa más madura). Cualquiera de las dos es aceptable si cumple el criterio de "nunca fingir unión".
- **Selección parcial / piezas no relacionadas**: si el usuario selecciona 3+ componentes donde algunos están cerca y otros lejos, no se especifica si el sistema debe intentar unir TODOS en una sola pieza (con múltiples bridges si hace falta) o rechazar la operación si algún par queda demasiado lejos. El implementador decide un criterio razonable (recomendación: intentar unir todos como un solo grafo de conexión, usando el componente más cercano como referencia para cada bridge necesario; si algo queda geométricamente imposible de conectar de forma segura, fallar con mensaje explicando cuál) y lo documenta.
- **Umbral de "tangente" vs "separado"**: no cuantificado. El implementador reutiliza el mismo criterio de tolerancia relativa ya establecido en M2-S03 (distancia mínima segmento-a-segmento relativa a la diagonal del bounding box) para decidir si dos piezas se tocan (unión booleana directa) o están separadas (requieren bridge).
- Ninguna otra ambigüedad bloqueante.

## Nota de orquestación
Corrida en modo autónomo (M2-S01..M2-S07 sin confirmación por paso, incluido merge de PRs), por autorización explícita del usuario. Esta es la tarjeta más compleja geométricamente de todo MVP2 — si el implementador encuentra que la validación post-operación (DoD: "nunca fingir unión") no puede garantizarse de forma robusta en el tiempo disponible, debe documentarlo claramente y el orquestador evaluará si corresponde dejar la tarjeta en estado "Bloqueada" en vez de forzar un cierre con una implementación insegura. Tarjetas que no puedan cerrarse quedan en "Bloqueada" en Notion con el motivo documentado.
