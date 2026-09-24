# M1-S04 · Threshold blanco y negro
URL: https://app.notion.com/p/3c1d77b26398815db84ce17d691cefde
Stack declarado: MISSING (no existe propiedad "Tecnología"/"Stack" en el board) — DERIVADO del cuerpo de la tarjeta: **React** (controles de threshold, histograma opcional, comparación, advertencia de máscara casi vacía/llena) · **ASP.NET Core Web API** (valida configuración, persiste en el proyecto, orquesta Python) · **Python/FastAPI + OpenCV** (threshold determinista, inversión, métricas foreground/background). Continúa sobre M1-S01+M1-S02+M1-S03, rama base `fix/3c1d77b2-preprocess-corrections` (M1-S03 más las dos rondas de corrección; todavía no mergeada a `main` vía PR #3, pero es la base más al día).

Nota de alcance: como las tarjetas anteriores de este board, es una feature de la aplicación de vectorización, no un sitio web de marketing. Mismo acuerdo ya establecido con el usuario: se implementa directamente con `web-implementer` (build/tests locales), sin `web-design-architect` ni `web-qa-auditor`.

## Requerimiento (transcripción del cuerpo de la tarjeta)

### Contexto
Convertir el preview preparado en una máscara binaria adecuada para vectorización B/N.

### Usuario podrá
Ajustar threshold y, si se valida técnicamente, elegir modo global/adaptativo; invertir blanco/negro; ver preview inmediato y resetear.

### React
Controles específicos, histograma opcional solo si aporta valor, comparación y advertencia cuando la máscara queda casi vacía/llena.

### ASP.NET Core Web API
Validar configuración, persistirla en el proyecto y orquestar Python. Exponer preview sin filtrar detalles internos del motor.

### Python/FastAPI
Implementar threshold determinista con OpenCV, inversión y métricas de porcentaje foreground/background. Mantener pipeline desacoplado.

### Contratos
image/version + threshold config → mask preview + métricas + parámetros efectivos.

### Pruebas
Imágenes claras/oscuras, transparencias, máscara vacía, máscara completa y repetibilidad.

### Definition of Done
El usuario puede producir y guardar una máscara B/N útil que será la entrada del tracing; errores y extremos se comunican claramente.

### Fuera de alcance
Generar SVG, editar paths y analizar color.

## Criterios de aceptación

De la propiedad "Criterio de aceptación": "Threshold ajustable con preview; resultado determinista y apto para el motor vectorial."

Ampliados por "Pruebas" y "Definition of Done" del cuerpo (declarados explícitamente, no derivados):
- [ ] Threshold ajustable desde React, con preview inmediato y reset.
- [ ] Inversión blanco/negro disponible.
- [ ] Resultado determinista: mismos parámetros + misma imagen de entrada → misma máscara byte a byte.
- [ ] Funciona correctamente con imágenes claras, oscuras y con transparencia.
- [ ] Casos extremos (máscara casi vacía o casi completa) se detectan y se comunican como advertencia, no como error silencioso.
- [ ] ASP.NET Core valida la configuración antes de llamar a Python y persiste la configuración en el proyecto (mismo patrón de versionado que M1-S03, ya corregido en esta rama: cada cambio confirmado — incluido un reset a valores ya vistos — produce una versión nueva, reutilizando bytes ya generados cuando corresponda).
- [ ] Python calcula métricas de porcentaje foreground/background junto con la máscara.
- [ ] El modo adaptativo (vs. global) es opcional — "si se valida técnicamente": si el implementador decide no incluirlo por complejidad/tiempo, debe declararlo como excepción, no implementarlo a medias.

## Referencias visuales / de marca
MISSING — no hay links ni adjuntos en la tarjeta.

## Umbrales de calidad
No aplican Lighthouse/axe (no es página de marketing). Verificación sustituta: ciclo local de `web-implementer` (build/typecheck/lint/tests, incluida repetibilidad determinista) contra "Pruebas" y "Definition of Done" de arriba.

## Ambigüedades detectadas
- Propiedad "Tecnología" no existe en el board — no bloqueante, stack declarado en el cuerpo.
- "Si se valida técnicamente" para el modo adaptativo es una condición abierta — el implementador decide si lo incluye y debe justificarlo (no es una ambigüedad que bloquee: alcanza con declarar la decisión).
- Qué constituye "casi vacía/llena" (umbral de porcentaje) no está cuantificado — el implementador debe elegir un valor razonable y declararlo como supuesto.
- Depende de que M1-S03 (preprocesamiento) esté disponible — ya lo está, en la rama base.
- Ninguna otra ambigüedad bloqueante.
