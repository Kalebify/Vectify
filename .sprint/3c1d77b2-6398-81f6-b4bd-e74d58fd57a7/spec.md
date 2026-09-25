# M1-S11 · Integración y pruebas E2E
URL: https://app.notion.com/p/3c1d77b2639881f6b4bde74d58fd57a7

**Naturaleza distinta a las tarjetas M1-S01 a M1-S10**: esta NO es una tarjeta de feature nueva. El propio cuerpo lo dice explícitamente: "Gate de calidad del MVP 1. No añadir features: demostrar que el flujo completo funciona con diseños reales." Es un sprint de integración/QA/documentación sobre TODO lo ya construido (M1-S01 a M1-S10, todos mergeados en `main` salvo el PR #12 de M1-S10, pendiente).

Stack: los tres (React, ASP.NET Core, Python), ya que el objetivo es ejercitar el pipeline completo end-to-end, no agregar código a una sola capa.

## Requerimiento (transcripción del cuerpo de la tarjeta)

### Contexto
Gate de calidad del MVP 1. No añadir features: demostrar que el flujo completo funciona con diseños reales.

### Escenarios E2E
Upload → preprocesamiento → threshold → vectorización → comparación → simplificación → Laser Checker → dimensiones mm → export SVG.

### Dataset
Crear fixtures representativos: logo, silueta, texto trazado, diseño con agujeros, ruido y caso problemático con paths/duplicados.

### Trabajo
Automatizar E2E donde sea estable; checklist manual para inspección visual y prueba de importación en software de fabricación cuando corresponda; medir tiempos y registrar defectos.

### Criterios de salida
Cero bugs bloqueantes; errores recuperables; README actualizado; Docker reproducible; tests de frontend/.NET/Python verdes; resultados de dataset documentados.

### Definition of Done
Un desarrollador nuevo puede clonar, arrancar y completar el MVP 1 siguiendo documentación sin conocimiento previo.

### Fuera de alcance
Cualquier feature del MVP 2.

## Criterios de aceptación

De la propiedad "Criterio de aceptación": "Upload → procesamiento → vectorización → validación → escala → export funciona E2E sin pasos manuales internos."

Ampliados por "Criterios de salida" y "Definition of Done" del cuerpo (declarados explícitamente, no derivados):
- [ ] Dataset de 6 imágenes fixture representativas, creadas y versionadas en el repo: logo (formas simples, pocos colores), silueta (contorno cerrado simple), texto trazado (múltiples subpaths pequeños, ya usado como concepto en M1-S07), diseño con agujeros (topología con huecos, ej. una dona/letra "O"/"A"), ruido (imagen con artefactos que ejercite denoise/threshold), y un caso problemático diseñado a propósito para disparar el Laser Checker (paths abiertos y/o duplicados detectables, M1-S08).
- [ ] Automatización E2E que encadene TODO el pipeline real vía HTTP contra la Web API real (mismo criterio que los E2E existentes de M1-S02, `tests/e2e/upload_e2e_test.mjs`/`real_stack_test.py`): upload → preprocess → threshold → vectorize → simplify → check → dimensions → export, verificando en cada paso que la salida de una etapa es válida como entrada de la siguiente (no solo que cada endpoint responda 200 aislado).
- [ ] Checklist MANUAL (no automatizable, documentado como tal) para: inspección visual del resultado en un navegador real, y prueba de importación del SVG exportado en software de fabricación externo (ej. un editor SVG/CAD que un desprendimiento como LightBurn usaría) — el spec dice "cuando corresponda", así que el implementador decide qué parte de esto es razonable ejecutar en este entorno (sin acceso a software de fabricación real) vs. dejar como checklist para que el humano lo corra.
- [ ] Medición de tiempos del pipeline completo (al menos informativo/loggeado, no necesariamente con un umbral de performance estricto no cuantificado por el spec).
- [ ] Registro de defectos encontrados durante este sprint (si los hay), con el mismo rigor que las rondas de corrección post-mortem ya aplicadas en M1-S07 (bugs de comandos SVG no soportados).
- [ ] `docker compose up --build` REALMENTE ejecutado y verificado — esta es la primera tarjeta desde M1-S01 donde esto deja de ser una excepción heredada aceptada: "Docker reproducible" es un criterio de salida EXPLÍCITO de esta tarjeta, no un supuesto a documentar como pendiente.
- [ ] Verificación en navegador real (Browser pane disponible en esta sesión) — igual que Docker, esta tarjeta es la primera donde "los tests de React usan jsdom" deja de ser suficiente per se: el objetivo explícito es que un desarrollador nuevo pueda "completar el MVP 1" de punta a punta, lo cual implica al menos una pasada real por la UI.
- [ ] README actualizado para reflejar el flujo E2E completo y el dataset (más allá de las secciones por sprint ya existentes) — sección nueva tipo "Flujo completo / E2E" que un desarrollador nuevo pueda seguir literalmente.
- [ ] `dotnet test`, `pytest`, `npm test` siguen en verde (ya lo están: 402/212/121 en `main` tras M1-S10, pendiente de que se mergee el PR #12).
- [ ] Cero bugs bloqueantes al cierre — cualquier bug encontrado durante este sprint que sea bloqueante debe corregirse antes de dar la tarjeta por terminada (no solo documentarse).

## Referencias visuales / de marca
MISSING — no hay links ni adjuntos en la tarjeta.

## Umbrales de calidad
No aplican Lighthouse/axe (no es un sitio de marketing). La verificación de ESTA tarjeta específicamente SÍ incluye, por primera vez en el sprint, `docker compose up --build` real y una pasada real por el navegador (Browser pane) — ver arriba, son criterios de salida explícitos de M1-S11, no opcionales como en tarjetas anteriores.

## Ambigüedades detectadas
- "Prueba de importación en software de fabricación" no es ejecutable en este entorno (sin acceso a LightBurn/software CAD real) — se documenta como checklist manual pendiente para el humano, no bloqueante para dar la tarjeta por Done, consistente con "cuando corresponda" del propio spec.
- Umbral de tiempo/performance no cuantificado — no bloqueante, se mide y documenta sin exigir un número específico.
- "Diseño con agujeros" y "caso problemático con paths/duplicados" son conceptualmente similares a fixtures ya usadas en tests unitarios de M1-S07/M1-S08 (sintéticas, generadas por código) — para este sprint, dado que el dataset debe ser representativo de "diseños reales" y pasar por upload real (PNG/JPG, no SVG sintético), el implementador debe generar imágenes RASTER (no SVGs a mano) que al vectorizarse produzcan esas características, documentando cómo se generó cada fixture.
- Ninguna otra ambigüedad bloqueante.

## Nota de orquestación
Dado que esta tarjeta es cualitativamente distinta (integración/QA, no una feature aislada), el enfoque de trabajo también va a ser distinto: además de delegar en `web-implementer` la generación del dataset y la automatización E2E, el orquestador (yo) voy a intentar correr `docker compose up --build` y hacer una pasada real por el navegador (Browser pane) personalmente, ya que son los dos criterios de salida que ninguna tarjeta anterior verificó y que están explícitamente dentro del alcance de esta.
