import { useCallback, useEffect, useRef, useState } from "react";
import { confirmPhysicalUnion, previewPhysicalUnion } from "../api/physicalUnionApi";
import { ApiClientError } from "../api/httpClient";
import type { ApiErrorResponse } from "../types/preprocess";
import type { PhysicalUnionErrorCode, PhysicalUnionPreviewResponse } from "../types/physicalUnion";

const GENERIC_ERROR_MESSAGE = "No se pudo completar la unión física. Intentá de nuevo.";

const KNOWN_ERROR_CODES: PhysicalUnionErrorCode[] = [
  "not_found",
  "component_not_found",
  "invalid_parameters",
  "physical_union_invalid_geometry",
  "physical_union_impossible",
  "invalid_input_svg",
  "svg_too_large",
  "timeout",
  "engine_unavailable",
  "invalid_response",
  "storage_failure",
  "internal_error",
  "network_error",
];

function isKnownErrorCode(code: string): code is PhysicalUnionErrorCode {
  return (KNOWN_ERROR_CODES as string[]).includes(code);
}

export type PhysicalUnionPhase = "idle" | "previewing" | "previewed" | "confirming" | "failed";

export interface UsePhysicalUnionState {
  /** layerGroupId -> fase actual de la operación en esa capa. */
  phaseByLayer: Record<string, PhysicalUnionPhase>;
  /** layerGroupId -> preview YA calculado (geometría real, sin persistir) para mostrar antes/después. */
  previewByLayer: Record<string, PhysicalUnionPreviewResponse | null>;
  /** layerGroupId -> código de error tipado de la última operación fallida (preview o confirm) de esa capa. */
  errorCodeByLayer: Record<string, PhysicalUnionErrorCode | null>;
  /** layerGroupId -> mensaje de error explicando POR QUÉ (spec.md: "la UI debe explicar por qué"). */
  errorMessageByLayer: Record<string, string | null>;
  /** Pide el preview (SIN persistir nada) de unir físicamente los componentIds indicados. */
  requestPreview: (layerGroupId: string, vectorId: string, componentIds: string[]) => void;
  /** Cancelar: puramente local, ningún llamado a la Web API -- descarta el preview sin dejar rastro. */
  cancelPreview: (layerGroupId: string) => void;
  /** Confirma la unión previamente previsualizada: persiste una VectorVersion nueva. */
  confirm: (layerGroupId: string, vectorId: string, componentIds: string[]) => void;
}

/**
 * Orquesta "Unión física" (M2-S06): preview SIEMPRE de solo lectura (no
 * persiste nada sin importar el resultado, ver PhysicalUnionService.cs),
 * confirmar/cancelar explícitos (cancelar es local, sin llamada a la Web
 * API), y advertencias claras si la unión no fue geométricamente posible
 * (spec.md: "nunca fingir unión" / "la UI debe explicar por qué"). Al
 * confirmar con éxito, notifica `onConfirmed` con el nuevo VectorId
 * resultante -- el caller (LayersPanel) decide qué hacer con eso (mostrar
 * la nueva geometría, recalcular componentes), este hook no conoce el
 * concepto de "capa"/VectorLayer.
 */
export function usePhysicalUnion(
  projectId: string,
  imageId: string,
  onConfirmed: (layerGroupId: string, newVectorId: string) => void,
): UsePhysicalUnionState {
  const [phaseByLayer, setPhaseByLayer] = useState<Record<string, PhysicalUnionPhase>>({});
  const [previewByLayer, setPreviewByLayer] = useState<Record<string, PhysicalUnionPreviewResponse | null>>({});
  const [errorCodeByLayer, setErrorCodeByLayer] = useState<Record<string, PhysicalUnionErrorCode | null>>({});
  const [errorMessageByLayer, setErrorMessageByLayer] = useState<Record<string, string | null>>({});

  const abortControllersRef = useRef<Set<AbortController>>(new Set());

  useEffect(() => {
    const controllers = abortControllersRef.current;
    return () => {
      for (const controller of controllers) {
        controller.abort();
      }
    };
  }, []);

  const handleError = useCallback((layerGroupId: string, error: unknown) => {
    if (error instanceof ApiClientError && error.isAborted) {
      return;
    }

    setPhaseByLayer((current) => ({ ...current, [layerGroupId]: "failed" }));

    if (error instanceof ApiClientError && error.body) {
      const body = error.body as Partial<ApiErrorResponse>;
      const code = typeof body.code === "string" && isKnownErrorCode(body.code) ? body.code : "internal_error";
      setErrorCodeByLayer((current) => ({ ...current, [layerGroupId]: code }));
      setErrorMessageByLayer((current) => ({ ...current, [layerGroupId]: body.message ?? GENERIC_ERROR_MESSAGE }));
    } else if (error instanceof ApiClientError && error.isNetworkError) {
      setErrorCodeByLayer((current) => ({ ...current, [layerGroupId]: "network_error" }));
      setErrorMessageByLayer((current) => ({
        ...current,
        [layerGroupId]: "No se pudo contactar a la Web API para unir físicamente las piezas.",
      }));
    } else {
      setErrorCodeByLayer((current) => ({ ...current, [layerGroupId]: "internal_error" }));
      setErrorMessageByLayer((current) => ({ ...current, [layerGroupId]: GENERIC_ERROR_MESSAGE }));
    }
  }, []);

  const requestPreview = useCallback(
    (layerGroupId: string, vectorId: string, componentIds: string[]) => {
      if (componentIds.length < 2) {
        return;
      }

      const controller = new AbortController();
      abortControllersRef.current.add(controller);

      setPhaseByLayer((current) => ({ ...current, [layerGroupId]: "previewing" }));
      setErrorCodeByLayer((current) => ({ ...current, [layerGroupId]: null }));
      setErrorMessageByLayer((current) => ({ ...current, [layerGroupId]: null }));

      previewPhysicalUnion(projectId, imageId, vectorId, componentIds, controller.signal)
        .then((response) => {
          abortControllersRef.current.delete(controller);
          setPreviewByLayer((current) => ({ ...current, [layerGroupId]: response }));
          setPhaseByLayer((current) => ({ ...current, [layerGroupId]: "previewed" }));
        })
        .catch((error: unknown) => {
          abortControllersRef.current.delete(controller);
          handleError(layerGroupId, error);
        });
    },
    [projectId, imageId, handleError],
  );

  const cancelPreview = useCallback((layerGroupId: string) => {
    // Puramente local: ningún llamado a la Web API, nada que "deshacer" del
    // lado del servidor -- preview nunca persistió nada (ver spec.md,
    // criterio de aceptación: "cancelar no genera ningún cambio").
    setPreviewByLayer((current) => ({ ...current, [layerGroupId]: null }));
    setPhaseByLayer((current) => ({ ...current, [layerGroupId]: "idle" }));
    setErrorCodeByLayer((current) => ({ ...current, [layerGroupId]: null }));
    setErrorMessageByLayer((current) => ({ ...current, [layerGroupId]: null }));
  }, []);

  const confirm = useCallback(
    (layerGroupId: string, vectorId: string, componentIds: string[]) => {
      const controller = new AbortController();
      abortControllersRef.current.add(controller);

      setPhaseByLayer((current) => ({ ...current, [layerGroupId]: "confirming" }));

      confirmPhysicalUnion(projectId, imageId, vectorId, componentIds, controller.signal)
        .then((response) => {
          abortControllersRef.current.delete(controller);
          setPreviewByLayer((current) => ({ ...current, [layerGroupId]: null }));
          setPhaseByLayer((current) => ({ ...current, [layerGroupId]: "idle" }));
          onConfirmed(layerGroupId, response.newVectorId);
        })
        .catch((error: unknown) => {
          abortControllersRef.current.delete(controller);
          handleError(layerGroupId, error);
        });
    },
    [projectId, imageId, handleError, onConfirmed],
  );

  return {
    phaseByLayer,
    previewByLayer,
    errorCodeByLayer,
    errorMessageByLayer,
    requestPreview,
    cancelPreview,
    confirm,
  };
}
