# Vectify — Frontend

React + TypeScript + Vite. Pantalla única de diagnóstico que consulta el
estado de la Web API (ASP.NET Core) y, a través de ella, del motor Python.
No implementa editor, upload ni vectorización — ver el README de la raíz
del repo para el alcance completo de este sprint.

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

- `src/api/httpClient.ts` — cliente HTTP centralizado hacia la Web API.
- `src/api/systemApi.ts` — llamada a `GET /api/v1/system/health`.
- `src/hooks/useSystemHealth.ts` — polling y traducción a los estados
  `loading` / `online` / `degraded` / `error`.
- `src/components/` — `StatusPill`, `ServiceCard`.
- `src/App.tsx` — pantalla Home/Diagnostics.

## Pruebas

Ejecutar npm test para las 8 pruebas de diagnóstico (Vitest y Testing Library). Simulan HTTP y verifican loading, servicios online/offline y recuperación. Ver ../tests/README.md para integración real y Docker.
