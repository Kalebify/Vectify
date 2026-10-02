import { useCallback, useEffect, useRef, useState } from "react";
import { saveWorkspace } from "../api/vectorDocumentApi";
import { ApiClientError } from "../api/httpClient";

/**
 * Máquina de estados real del botón Guardar (M2.2-S05), reemplazando el placeholder estático de
 * `EditorHeader.tsx` ("Sin guardado automático todavía"):
 *
 * - `idle`: todavía no hubo ninguna mutación en esta sesión del Workspace (o se acaba de cargar
 *   el documento).
 * - `dirty`: hubo al menos una mutación (rename/toggle/reorder/operación) desde el último Save
 *   exitoso -- el usuario debe disparar el Save con el botón, NUNCA automático (fuera de
 *   alcance de esta tarjeta, ver spec.md "Fuera de alcance": "Autosave/debounce").
 * - `saving`: Save en curso.
 * - `saved`: Save confirmado por el backend (200/201 real) -- NUNCA optimista, ver
 *   `GENERIC_ERROR_MESSAGE`/`save` más abajo: el estado solo pasa a "saved" dentro del `.then`
 *   de la promesa real, nunca antes de llamarla.
 * - `error`: el Save falló (red/409/422/500) -- mensaje visible, reintentable, y el documento
 *   vuelve a `dirty` (no se pierde el intento: el usuario puede reintentar sin perder cambios).
 */
export type WorkspaceSaveState = "idle" | "dirty" | "saving" | "saved" | "error";

const GENERIC_ERROR_MESSAGE = "No se pudo guardar el documento. Intentá de nuevo.";

export interface UseWorkspaceSaveParams {
  /** Project.Id v2 ya conocido (sesión reabierta, o ya guardado antes en esta misma sesión) -- null si todavía no existe. */
  initialSavedProjectId: string | null;
  /** Nombre a usar SOLO en el primer Save (creación del Project v2) -- ignorado en Saves subsiguientes. */
  projectName: string;
  classicProjectId: string;
  imageId: string;
  paletteId: string | null;
  paletteVersion: number | null;
  /** Último DimensionResponse.DimensionId aplicado en esta sesión del Workspace, o null si nunca se aplicaron dimensiones físicas. */
  dimensionId: string | null;
}

export interface UseWorkspaceSaveState {
  state: WorkspaceSaveState;
  errorMessage: string | null;
  /** Project.Id v2 vigente -- null hasta que el primer Save exitoso lo resuelva. */
  savedProjectId: string | null;
  /** Marca el documento como "dirty" (hay cambios sin guardar) -- no-op si ya está guardando. */
  markDirty: () => void;
  /** Dispara el Save explícito. No-op si no hay paleta/versión resuelta todavía (documento no cargado). */
  save: () => void;
}

export function useWorkspaceSave(params: UseWorkspaceSaveParams): UseWorkspaceSaveState {
  const [state, setState] = useState<WorkspaceSaveState>("idle");
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [savedProjectId, setSavedProjectId] = useState<string | null>(params.initialSavedProjectId);

  // Último valor de cada parámetro en una ref: `save()` es estable (no se recrea en cada
  // render) pero siempre debe mandar los valores VIGENTES al momento del click, no los
  // capturados en la clausura de cuando se creó el callback.
  const latestParamsRef = useRef(params);
  latestParamsRef.current = params;

  const abortControllerRef = useRef<AbortController | null>(null);
  useEffect(() => {
    return () => abortControllerRef.current?.abort();
  }, []);

  const markDirty = useCallback(() => {
    setState((current) => (current === "saving" ? current : "dirty"));
  }, []);

  const save = useCallback(() => {
    const current = latestParamsRef.current;
    if (!current.paletteId || current.paletteVersion === null) {
      return;
    }

    abortControllerRef.current?.abort();
    const controller = new AbortController();
    abortControllerRef.current = controller;

    setState("saving");
    setErrorMessage(null);

    saveWorkspace(
      {
        projectId: savedProjectId,
        name: savedProjectId ? null : current.projectName,
        classicProjectId: current.classicProjectId,
        imageId: current.imageId,
        paletteId: current.paletteId,
        paletteVersion: current.paletteVersion,
        dimensionId: current.dimensionId,
      },
      controller.signal,
    )
      .then((response) => {
        // "saved" solo tras esta confirmación real del backend -- nunca antes (requisito
        // explícito de la tarjeta).
        setSavedProjectId(response.projectId);
        setState("saved");
      })
      .catch((error: unknown) => {
        if (error instanceof ApiClientError && error.isAborted) {
          return;
        }

        // El intento de guardar NO se pierde: el documento vuelve a "dirty" (no "idle"),
        // así que el usuario puede reintentar sin que el botón Guardar quede inactivo.
        setState("error");
        if (error instanceof ApiClientError && !error.isNetworkError && error.body) {
          const body = error.body as { message?: string };
          setErrorMessage(body.message ?? GENERIC_ERROR_MESSAGE);
        } else {
          setErrorMessage(GENERIC_ERROR_MESSAGE);
        }
      });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [savedProjectId]);

  return { state, errorMessage, savedProjectId, markDirty, save };
}
