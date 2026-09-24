# M1-S02 · Carga y almacenamiento de imágenes
URL: https://app.notion.com/p/3c1d77b2639881099fb0ef873c136011
Stack declarado: MISSING (no existe propiedad "Tecnología"/"Stack" en el board) — DERIVADO del cuerpo de la tarjeta: **React** (Dropzone, preview, progreso — consume solo ASP.NET Core) · **ASP.NET Core Web API** (endpoint de upload/proyecto, validación, `IFileStorage`) · **Python no participa en este sprint**. Continúa sobre el skeleton de M1-S01 (`sprint/3c1d77b2-skeleton-net-python`, ya en Done).

Nota de alcance: como M1-S01, esta tarjeta es una feature de la aplicación de vectorización (upload de imágenes), no un sitio web de marketing. Por el mismo acuerdo ya establecido con el usuario para este board, se implementa directamente con `web-implementer` (build/tests locales como verificación), sin pasar por `web-design-architect` ni `web-qa-auditor`.

## Requerimiento (transcripción del cuerpo de la tarjeta)

### Contexto
Primer flujo funcional de entrada. Debe crear un proyecto a partir de una imagen sin procesarla todavía.

### Usuario podrá
Arrastrar o seleccionar PNG/JPG/WEBP, ver nombre/tamaño/preview, cancelar o confirmar la carga y recibir errores comprensibles.

### React
Crear componente Dropzone, preview, progreso, validaciones UX y pantalla de proyecto recién creado. React solo consume ASP.NET Core.

### ASP.NET Core Web API
Endpoint versionado de upload/proyecto; validar MIME, extensión, tamaño y archivo vacío; generar IDs; almacenar original mediante abstracción `IFileStorage`; devolver metadatos tipados. Preparar almacenamiento local para desarrollo y contrato sustituible por S3-compatible.

### Python
No participa en este sprint.

### Contratos
Definir request multipart y respuesta con projectId, imageId, filename, mimeType, bytes, width/height cuando estén disponibles y estado.

### Errores
Formato no soportado, tamaño máximo, upload interrumpido, archivo corrupto, fallo de storage y duplicación accidental.

### Pruebas
Unitarias de validadores; integración de endpoint/storage; frontend para selección/error; E2E de carga válida e inválida.

### Definition of Done
Una imagen válida crea proyecto y original recuperable; el original nunca se modifica; errores son controlados; tests pasan y documentación del endpoint está actualizada.

### Fuera de alcance
Preprocesamiento, quitar fondo, threshold, vectorización, autenticación y almacenamiento cloud productivo.

## Criterios de aceptación

De la propiedad "Criterio de aceptación": "Archivo válido se carga, valida y recupera; formatos/tamaños inválidos muestran error controlado."

Ampliados por "Pruebas" y "Definition of Done" del cuerpo (declarados explícitamente, no derivados):
- [ ] PNG/JPG/WEBP válidos se cargan, crean proyecto y el original es recuperable sin modificarse.
- [ ] Formato no soportado, tamaño excedido, archivo vacío/corrupto y upload interrumpido producen error controlado y comprensible (React y API).
- [ ] Fallo de storage y duplicación accidental están controlados.
- [ ] Endpoint versionado devuelve metadatos tipados (projectId, imageId, filename, mimeType, bytes, width/height cuando estén disponibles, estado).
- [ ] `IFileStorage` abstrae el almacenamiento: implementación local para desarrollo, contrato sustituible por S3-compatible.
- [ ] Dropzone en React: arrastrar/seleccionar, preview, progreso, cancelar/confirmar, solo consume ASP.NET Core (nunca Python).
- [ ] Tests unitarios de validadores, integración de endpoint/storage, tests de frontend para selección/error, y E2E de carga válida e inválida — todos pasan.
- [ ] Documentación del endpoint (Swagger/README) actualizada.

## Referencias visuales / de marca
MISSING — no hay links ni adjuntos en la tarjeta.

## Umbrales de calidad
No aplican los umbrales Lighthouse/axe por defecto (no es una página pública de marketing). Verificación sustituta: ciclo local de `web-implementer` (build, typecheck, lint, tests unitarios/integración/E2E) contra las "Pruebas" y "Definition of Done" de arriba.

## Ambigüedades detectadas
- Propiedad "Tecnología" no existe en el board — no bloqueante: el stack está declarado explícitamente en el cuerpo de la tarjeta.
- Tamaño máximo de archivo y lista exacta de MIME types permitidos no están cuantificados en la tarjeta ("PNG/JPG/WEBP", sin límite de bytes explícito) — se tratará como DERIVADO por el implementador si hace falta un valor concreto, y debe declararse como supuesto en su reporte.
- Ninguna otra ambigüedad bloqueante.
