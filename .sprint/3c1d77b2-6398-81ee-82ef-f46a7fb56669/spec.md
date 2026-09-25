# M1-S07 · Simplificación de nodos
URL: https://app.notion.com/p/3c1d77b2639881ee82eff46a7fb56669
Stack declarado: MISSING (no existe propiedad "Tecnología"/"Stack" en el board) — DERIVADO del cuerpo de la tarjeta y de la propiedad "Área" (Vector, Python, Frontend): **Python/Vector** (algoritmo de simplificación de curvas/contornos, encapsulado detrás de una interfaz, análogo al patrón `VectorEngine` de M1-S05) · **ASP.NET Core Web API** (nueva `VectorVersion` al aplicar, historial conservado, validación de tolerancia, orquestación del motor) · **React** (control de tolerancia/preset, preview con métricas antes/después, confirmar/cancelar). Continúa sobre M1-S01 a M1-S06 ya mergeados en `main`.

Nota de alcance: como las tarjetas anteriores de este board, es una feature de la aplicación de vectorización, no un sitio web de marketing. Mismo acuerdo ya establecido con el usuario: se implementa directamente con `web-implementer` (build/tests locales), sin `web-design-architect` ni `web-qa-auditor`.

## Requerimiento (transcripción del cuerpo de la tarjeta)

### Contexto
El tracing puede generar miles de nodos. Este sprint reduce complejidad sin degradar de forma inaceptable el diseño.

### Usuario podrá
Elegir tolerancia/preset Bajo-Medio-Alto, ver preview y contador antes/después, aplicar o cancelar.

### React
Control de tolerancia, métricas, comparación y confirmación. No sobrescribir versión anterior.

### ASP.NET Core
Crear nueva VectorVersion al aplicar; conservar historial; validar tolerancia y orquestar motor.

### Python/Vector
Implementar simplificación de curvas/contornos detrás de interfaz; preservar paths cerrados, agujeros y topología en los casos soportados; medir nodeCount y reducción porcentual.

### Pruebas
Curvas suaves, esquinas, agujeros, texto trazado y tolerancias extremas. Verificar que el diseño no se rompe.

### Definition of Done
Simplificación reduce nodos mediblemente, preview es reversible y aplicar crea una versión nueva válida.

### Fuera de alcance
Edición manual de nodos y optimización específica de color.

## Criterios de aceptación

De la propiedad "Criterio de aceptación": "Se muestran nodos antes/después y la simplificación no rompe paths válidos."

Ampliados por "Pruebas" y "Definition of Done" del cuerpo (declarados explícitamente, no derivados):
- [ ] Presets de tolerancia Bajo/Medio/Alto seleccionables (el spec no cuantifica los valores numéricos de cada preset — el implementador debe elegir valores razonables para el algoritmo elegido, ej. epsilon de Douglas-Peucker, y documentarlos como supuesto).
- [ ] Preview antes de aplicar: muestra nodeCount antes/después y % de reducción, sin persistir cambios hasta confirmar.
- [ ] Preview es reversible: cancelar no deja rastro (no crea versión, no modifica el estado persistido).
- [ ] Aplicar crea una nueva `VectorVersion` (no sobrescribe la anterior) — mismo patrón de historial versionado ya usado en Preprocessing/Threshold/Vectorization.
- [ ] Simplificación preserva topología en los casos soportados: paths cerrados, agujeros (holes) y curvas suaves no se rompen ni se abren.
- [ ] Casos de prueba obligatorios: curvas suaves, esquinas (ángulos agudos), agujeros, texto trazado (paths con múltiples subpaths pequeños) y tolerancias extremas (mínima y máxima) — verificar que el path sigue siendo válido (bien formado, sin auto-intersección introducida que no existiera antes, cerrado si era cerrado) en cada caso.
- [ ] Validación de tolerancia en ASP.NET Core antes de invocar el motor (rango válido, no solo el preset sino también si se expone un valor numérico custom).
- [ ] Reducción de nodos es medible y reportada (nodeCount antes, nodeCount después, % reducción) tanto en la respuesta del motor Python como en lo que ve el usuario en React.

## Referencias visuales / de marca
MISSING — no hay links ni adjuntos en la tarjeta.

## Umbrales de calidad
No aplican Lighthouse/axe por defecto del sistema en el sentido de "sitio de marketing". Verificación sustituta: ciclo local de `web-implementer` (build/typecheck/lint/tests) contra "Pruebas" y "Definition of Done" de arriba, igual que en tarjetas anteriores.

## Ambigüedades detectadas
- Propiedad "Tecnología" no existe en el board — no bloqueante, stack derivado del cuerpo y del campo "Área".
- Valores numéricos concretos de los presets Bajo/Medio/Alto no están cuantificados — no bloqueante, el implementador elige y documenta como supuesto (consistente con el patrón ya usado para thresholds/umbrales en tarjetas anteriores).
- Algoritmo de simplificación de curvas no especificado (Douglas-Peucker, Visvalingam-Whyatt, u otro) — no bloqueante, el implementador elige uno estándar y documenta la elección y por qué preserva topología en los casos soportados.
- "Texto trazado" como caso de prueba probablemente se refiere a paths con múltiples subpaths pequeños (glifos vectorizados) — no bloqueante, se interpreta así salvo mejor criterio del implementador.
- Ninguna otra ambigüedad bloqueante.
