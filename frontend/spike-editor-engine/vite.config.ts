import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// Config del spike M2.1-S05, deliberadamente aislada de frontend/vite.config.ts
// (root propio, sin alias ni proxy compartidos con el frontend de producción).
export default defineConfig({
  plugins: [react()],
  // fixtures/ sirve tanto de "public dir" (los .svg se piden por fetch en
  // runtime, ver src/shared/loadLayersFromSvg.ts) como de carpeta donde
  // vive el script que los generó (generate-medium-complexity.mjs, que no
  // se ejecuta en el browser, solo queda ahí como documentación/repro).
  publicDir: "fixtures",
  server: {
    port: 5183,
  },
  build: {
    outDir: "dist",
  },
});
