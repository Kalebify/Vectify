# Vectify — Frontend

React + TypeScript + Vite. Pantalla de diagnóstico (estado de la Web API y,
a través de ella, del motor Python) más el flujo de carga de imagen (M1-S02:
Dropzone → preview → confirmar/cancelar → proyecto creado) y el panel de
preprocesamiento (M1-S03: sliders de contraste/brillo/denoise/escala de grises
con preview antes/después). No implementa editor ni vectorización todavía —
ver el README de la raíz del repo para el alcance completo de este sprint.

## Arranque

```bash
cp .env.example .env
npm install
npm run dev
```

## Scripts

- `npm run dev` — servidor de desarrollo (Vite).
- `npm run build` — typecheck (`tsc -b`) + build de producción.
- `npm run lint` — oxlint.
- `npm run preview` — sirve el build de producción localmente.

## Estructura

- `src/api/httpClient.ts` — cliente HTTP centralizado hacia la Web API
  (`request`/fetch para JSON, `uploadFile`/XMLHttpRequest para multipart con
  progreso).
- `src/api/systemApi.ts` — llamada a `GET /api/v1/system/health`.
- `src/api/projectsApi.ts` — `POST /api/v1/projects` (carga de imagen, con
  soporte de `Idempotency-Key`) y la URL del original.
- `src/api/preprocessApi.ts` — `POST .../preview` y la URL de cada preview
  generado (M1-S03).
- `src/hooks/useSystemHealth.ts` — polling y traducción a los estados
  `loading` / `online` / `degraded` / `error`.
- `src/hooks/useImageUpload.ts` — orquesta selección → validación de
  cliente → confirmar/cancelar → progreso → resultado del flujo de carga.
- `src/hooks/usePreprocess.ts` — orquesta el envío de parámetros de
  preprocesamiento y el estado del preview resultante.
- `src/lib/validateImageFile.ts` — validación de cliente (best-effort; la Web
  API vuelve a validar todo antes de guardar nada).
- `src/components/` — `StatusPill`, `ServiceCard` (diagnóstico);
  `upload/` (`Dropzone`, `FilePreview`, `UploadProgress`,
  `ProjectCreatedCard`, `UploadPanel`); `preprocess/` (`ParameterControls`,
  `ImageComparison`, `PreprocessPanel`).
- `src/App.tsx` — pantalla principal: diagnóstico + `UploadPanel` +
  `PreprocessPanel` una vez que hay un proyecto creado.

## Pruebas

`npm test` corre la suite de Vitest + Testing Library (diagnóstico, carga de
imagen en `UploadPanel.test.tsx` y preprocesamiento en
`PreprocessPanel.test.tsx`), simulando HTTP (fetch mockeado y un
`FakeXMLHttpRequest` propio para progreso de subida) y verificando loading,
servicios online/offline, recuperación, validación/errores de carga,
Idempotency-Key, debounce de sliders y los estados `loading`/`ready`/`error`
del panel de preprocesamiento. Ver ../tests/README.md para integración real
y Docker.
