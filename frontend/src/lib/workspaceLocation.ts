/**
 * Ruteo mínimo del Workspace (M2.1-S06, persistencia M2.1-S08): la URL
 * identifica projectId/imageId/paletteId vía query string, sincronizada a
 * mano con `URLSearchParams` + `History.pushState` -- sin agregar
 * `react-router` ni ninguna otra dependencia de ruteo.
 *
 * Justificación (ver IMPL.md para el detalle completo): hoy la app tiene un
 * solo estado de navegación real ("Workspace abierto con estos 3 ids" vs.
 * "flujo clásico de Upload hacia abajo"), sin rutas anidadas, sin parámetros
 * dinámicos adicionales y sin necesidad de un layout por ruta -- exactamente
 * el mismo criterio ya aplicado en M2.1-S07 para Drag & Drop nativo (preferir
 * la plataforma antes que una dependencia nueva cuando el caso de uso es
 * acotado). Si una tarjeta futura agrega rutas anidadas reales (ej. M2.2-S08,
 * "Mis Proyectos", con su propia URL y navegación entre proyectos), ahí sí
 * vale la pena reevaluar `react-router`.
 */

export interface WorkspaceLocation {
  projectId: string;
  imageId: string;
  paletteId: string;
}

const PROJECT_PARAM = "projectId";
const IMAGE_PARAM = "imageId";
const PALETTE_PARAM = "paletteId";

/**
 * Lee projectId/imageId/paletteId de una query string (default: la URL
 * actual del navegador). Devuelve `null` si falta cualquiera de los tres --
 * un deep-link parcial no alcanza para reconstruir el Workspace.
 */
export function readWorkspaceLocation(search: string = window.location.search): WorkspaceLocation | null {
  const params = new URLSearchParams(search);
  const projectId = params.get(PROJECT_PARAM);
  const imageId = params.get(IMAGE_PARAM);
  const paletteId = params.get(PALETTE_PARAM);

  if (!projectId || !imageId || !paletteId) {
    return null;
  }

  return { projectId, imageId, paletteId };
}

/** Construye la query string (con el `?` inicial) para una ubicación del Workspace. */
export function buildWorkspaceSearch(location: WorkspaceLocation): string {
  const params = new URLSearchParams();
  params.set(PROJECT_PARAM, location.projectId);
  params.set(IMAGE_PARAM, location.imageId);
  params.set(PALETTE_PARAM, location.paletteId);
  return `?${params.toString()}`;
}

/**
 * Actualiza la URL del navegador para reflejar el Workspace abierto (push,
 * no replace -- un reload inmediatamente después reconstruye la misma
 * sesión, ver spec.md punto 4 del alcance).
 */
export function pushWorkspaceLocation(location: WorkspaceLocation): void {
  const search = buildWorkspaceSearch(location);
  window.history.pushState({}, "", `${window.location.pathname}${search}`);
}

/** Vuelve a la URL del flujo clásico (sin query params) -- usado al cerrar el Workspace. */
export function clearWorkspaceLocation(): void {
  window.history.pushState({}, "", window.location.pathname);
}
