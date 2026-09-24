# M1-S03 · Preprocesamiento de imagen
URL: https://app.notion.com/p/3c1d77b2639881a18df9c2ecd0f5653f
Stack declarado: MISSING (no existe propiedad "Tecnología"/"Stack" en el board) — DERIVADO del cuerpo de la tarjeta: **React** (panel de controles/sliders, debounce, comparación original/procesado) · **ASP.NET Core Web API** (endpoint de preview, validación de rangos, orquesta la llamada a Python, versiona configuración) · **Python/FastAPI + OpenCV** (pipeline determinista: grayscale, contraste/normalización, suavizado/denoise, preview). Continúa sobre M1-S01 (skeleton) y M1-S02 (upload), rama base `sprint/3c1d77b2-carga-imagenes`.

Nota de alcance: como M1-S01/M1-S02, esta tarjeta es una feature de la aplicación de vectorización, no un sitio web de marketing. Mismo acuerdo ya establecido con el usuario para este board: se implementa directamente con `web-implementer` (build/tests locales), sin `web-design-architect` ni `web-qa-auditor`.

## Requerimiento (transcripción del cuerpo de la tarjeta)

### Contexto
Preparar una copia de trabajo de la imagen para mejorar el tracing sin alterar jamás el original.

### Usuario podrá
Ver original y preview; ajustar escala de grises, contraste, brillo/suavizado y reducción de ruido; restablecer valores.

### React
Panel de controles con sliders, debounce, loading y comparación original/procesado. Guardar parámetros como estado del proyecto, no píxeles manipulados en navegador.

### ASP.NET Core Web API
Endpoint para solicitar preview y guardar parámetros; validar rangos; orquestar llamada a Python; cachear/referenciar preview; mantener versionado de configuración.

### Python/FastAPI
Implementar pipeline OpenCV determinista: lectura segura, grayscale cuando aplique, contraste/normalización, suavizado/denoise y generación de preview. Separar funciones puras y servicio de pipeline.

### Contratos
Entrada: imageId + parámetros versionados. Salida: previewId/URL, dimensiones, parámetros efectivos y métricas básicas.

### Errores y límites
Imagen corrupta, memoria, dimensiones excesivas, parámetros inválidos y timeout.

### Pruebas
Golden images pequeñas; mismos parámetros → mismo resultado; original intacto; límites de sliders; integración .NET↔Python.

### Definition of Done
Usuario modifica parámetros y obtiene previews reproducibles; puede resetear; original permanece intacto; tests Python/.NET/React pasan.

### Fuera de alcance
Threshold final, eliminación de fondo avanzada, vectorización y color por capas.

## Criterios de aceptación

De la propiedad "Criterio de aceptación": "Usuario modifica parámetros y obtiene preview reproducible sin alterar el original."

Ampliados por "Pruebas" y "Definition of Done" del cuerpo (declarados explícitamente, no derivados):
- [ ] Grayscale, contraste, brillo/suavizado y reducción de ruido son ajustables desde React con sliders + debounce.
- [ ] El original nunca se modifica — el preprocesamiento trabaja sobre una copia/derivado.
- [ ] Mismos parámetros producen siempre el mismo resultado (pipeline determinista, verificable con golden images pequeñas).
- [ ] "Resetear" vuelve a los valores por defecto.
- [ ] Parámetros fuera de rango, imagen corrupta, dimensiones excesivas y timeout producen error controlado.
- [ ] Endpoint de ASP.NET Core valida rangos antes de llamar a Python, versiona la configuración de parámetros y cachea/referencia el preview generado.
- [ ] Pipeline en Python separa funciones puras (transformaciones) del servicio que las orquesta.
- [ ] Tests Python (golden images + límites), .NET (integración con Python) y React pasan.

## Referencias visuales / de marca
MISSING — no hay links ni adjuntos en la tarjeta.

## Umbrales de calidad
No aplican Lighthouse/axe (no es página de marketing). Verificación sustituta: ciclo local de `web-implementer` (build/typecheck/lint/tests, incluidas golden images deterministas) contra "Pruebas" y "Definition of Done" de arriba.

## Ambigüedades detectadas
- Propiedad "Tecnología" no existe en el board — no bloqueante, stack declarado en el cuerpo.
- Rangos numéricos exactos de los sliders (contraste, brillo, suavizado, reducción de ruido) y el algoritmo específico de denoise/suavizado no están cuantificados — el implementador debe elegir valores/algoritmo razonables (OpenCV) y declararlo como supuesto.
- "Dimensiones excesivas" y el timeout concreto no están cuantificados — el implementador debe elegir un límite razonable y declararlo como supuesto.
- Depende de que M1-S02 (upload) esté disponible en la rama base — ya lo está (`sprint/3c1d77b2-carga-imagenes`).
- Ninguna otra ambigüedad bloqueante.
