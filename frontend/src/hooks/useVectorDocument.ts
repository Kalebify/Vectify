import { useCallback, useEffect, useRef, useState } from "react";
import { getColorPalette } from "../api/colorPaletteApi";
import { getConsolidatedVectorLayers } from "../api/consolidatedVectorLayersApi";
import { ApiClientError } from "../api/httpClient";
import { getVectorLayers } from "../api/vectorLayersApi";
import type { ManufacturingOperationValue } from "../types/manufacturingOperations";

/**
 * `VectorDocument`: estado de dominio del Workspace (M2.1-S06), agregando en
 * un solo objeto lo que hoy son 3 llamadas de solo lectura independientes
 * (paleta confirmada + conjunto de capas + info consolidada). Ver spec.md,
 * "Ambigüedades detectadas": esto es una agregación del lado del CLIENTE
 * sobre endpoints YA EXISTENTES -- no hay ninguna entidad `VectorDocument`
 * nueva en el backend, ni se persiste acá (esa es M2.1-S08, la tarjeta
 * siguiente, "Persistencia del VectorDocument y reapertura").
 *
 * Todos los paneles del Workspace (VectorCanvas, EditorLayersPanel,
 * PaletteBar, InspectorPanel, PreviewNavigator) consumen ESTE hook como
 * única fuente de verdad -- ninguno vuelve a pedir la paleta/capas por su
 * cuenta ni guarda una copia divergente.
 */
export type VectorDocumentStatus = "idle" | "loading" | "ready" | "empty" | "error";

/** Por qué el documento está "empty" (estado vacío honesto, spec.md: "nunca datos simulados"). */
export type VectorDocumentEmptyReason =
  | "no_palette_selected"
  | "palette_not_found"
  | "palette_not_confirmed"
  | "layers_not_generated";

export interface VectorDocumentLayer {
  groupId: string;
  name: string;
  colorHex: string;
  fill: string;
  vectorId: string;
  svgUrl: string;
  pathCount: number;
  componentCount: number | null;
  manufacturingOperation: ManufacturingOperationValue;
  order: number;
  areaPercent: number;
  hasPartialAlpha: boolean;
  isExcluded: boolean;
}

export interface VectorDocument {
  projectId: string;
  imageId: string;
  paletteId: string;
  paletteVersion: number;
  layerSetId: string;
  version: number;
  sourceWidthPx: number;
  sourceHeightPx: number;
  layers: VectorDocumentLayer[];
}

export interface UseVectorDocumentState {
  status: VectorDocumentStatus;
  document: VectorDocument | null;
  emptyReason: VectorDocumentEmptyReason | null;
  errorMessage: string | null;
  /** Vuelve a pedir todo desde cero (ej. después de generar capas desde otro panel, o el botón "Reintentar" del estado de error). */
  reload: () => void;

  /** groupId -> visible. Estado de vista COMPARTIDO por todos los paneles (mismo patrón que useVectorLayers.visibility de M2-S02/M2.1-S04), nunca escrito en 2 lugares. */
  visibility: Record<string, boolean>;
  toggleVisibility: (groupId: string) => void;
  isolate: (groupId: string) => void;
  showAll: () => void;

  /** Selección COMPARTIDA de "qué capa se está inspeccionando" (Canvas/LayersPanel/PaletteBar/Inspector). */
  selectedGroupId: string | null;
  selectGroup: (groupId: string | null) => void;
}

const GENERIC_ERROR_MESSAGE = "No se pudo cargar el documento del proyecto. Intentá de nuevo.";

function toDocument(
  projectId: string,
  imageId: string,
  paletteVersion: number,
  layerSetResponse: Awaited<ReturnType<typeof getVectorLayers>>,
  consolidated: Awaited<ReturnType<typeof getConsolidatedVectorLayers>>,
  paletteGroupsById: Map<string, { areaPercent: number; hasPartialAlpha: boolean; isExcluded: boolean }>,
): VectorDocument {
  const consolidatedById = new Map(consolidated.layers.map((layer) => [layer.id, layer]));

  const layers: VectorDocumentLayer[] = layerSetResponse.layers.map((layer, index) => {
    const info = consolidatedById.get(layer.groupId);
    const paletteInfo = paletteGroupsById.get(layer.groupId);
    return {
      groupId: layer.groupId,
      name: layer.name,
      colorHex: layer.colorHex,
      fill: info?.fill ?? layer.colorHex,
      vectorId: layer.vectorId,
      svgUrl: info?.svgUrl ?? layer.svgUrl,
      pathCount: info?.pathCount ?? 0,
      componentCount: info?.componentCount ?? null,
      manufacturingOperation: (info?.manufacturingOperation as ManufacturingOperationValue | undefined) ?? "unassigned",
      order: info?.order ?? index,
      areaPercent: paletteInfo?.areaPercent ?? layer.areaPercent,
      hasPartialAlpha: paletteInfo?.hasPartialAlpha ?? layer.hasPartialAlpha,
      isExcluded: paletteInfo?.isExcluded ?? false,
    };
  });

  return {
    projectId,
    imageId,
    paletteId: layerSetResponse.paletteId,
    paletteVersion,
    layerSetId: layerSetResponse.layerSetId,
    version: layerSetResponse.version,
    sourceWidthPx: layerSetResponse.sourceWidthPx,
    sourceHeightPx: layerSetResponse.sourceHeightPx,
    layers,
  };
}

export function useVectorDocument(
  projectId: string,
  imageId: string,
  paletteId: string | null,
): UseVectorDocumentState {
  const [status, setStatus] = useState<VectorDocumentStatus>("idle");
  const [document, setDocument] = useState<VectorDocument | null>(null);
  const [emptyReason, setEmptyReason] = useState<VectorDocumentEmptyReason | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [visibility, setVisibility] = useState<Record<string, boolean>>({});
  const [selectedGroupId, setSelectedGroupId] = useState<string | null>(null);

  const abortControllerRef = useRef<AbortController | null>(null);
  const requestedForRef = useRef<string | null>(null);

  useEffect(() => {
    return () => abortControllerRef.current?.abort();
  }, []);

  const load = useCallback(() => {
    if (!paletteId) {
      setStatus("empty");
      setEmptyReason("no_palette_selected");
      setDocument(null);
      return;
    }

    abortControllerRef.current?.abort();
    const controller = new AbortController();
    abortControllerRef.current = controller;

    setStatus("loading");
    setEmptyReason(null);
    setErrorMessage(null);

    (async () => {
      const palette = await getColorPalette(projectId, imageId, paletteId, controller.signal);

      if (!palette.isConfirmed) {
        setStatus("empty");
        setEmptyReason("palette_not_confirmed");
        return;
      }

      let layerSetResponse;
      try {
        layerSetResponse = await getVectorLayers(projectId, imageId, paletteId, controller.signal);
      } catch (error) {
        if (error instanceof ApiClientError && !error.isAborted && !error.isNetworkError) {
          const body = error.body as { code?: string } | undefined;
          if (body?.code === "not_found") {
            setStatus("empty");
            setEmptyReason("layers_not_generated");
            return;
          }
        }
        throw error;
      }

      const consolidated = await getConsolidatedVectorLayers(projectId, imageId, paletteId, controller.signal);

      const paletteGroupsById = new Map(
        palette.groups.map((group) => [
          group.groupId,
          { areaPercent: group.areaPercent, hasPartialAlpha: group.hasPartialAlpha, isExcluded: group.isExcluded },
        ]),
      );

      const doc = toDocument(projectId, imageId, palette.version, layerSetResponse, consolidated, paletteGroupsById);

      if (doc.layers.length === 0) {
        setStatus("empty");
        setEmptyReason("layers_not_generated");
        return;
      }

      setDocument(doc);
      setVisibility(Object.fromEntries(doc.layers.map((layer) => [layer.groupId, true])));
      setStatus("ready");
    })().catch((error: unknown) => {
      abortControllerRef.current = null;
      if (error instanceof ApiClientError) {
        if (error.isAborted) {
          return;
        }
        if (!error.isNetworkError && error.body) {
          const body = error.body as { code?: string; message?: string };
          if (body.code === "not_found") {
            setStatus("empty");
            setEmptyReason("palette_not_found");
            return;
          }
          setErrorMessage(body.message ?? GENERIC_ERROR_MESSAGE);
          setStatus("error");
          return;
        }
      }
      setErrorMessage(GENERIC_ERROR_MESSAGE);
      setStatus("error");
    });
  }, [projectId, imageId, paletteId]);

  useEffect(() => {
    const key = `${projectId}:${imageId}:${paletteId ?? ""}`;
    if (requestedForRef.current === key) {
      return;
    }
    requestedForRef.current = key;
    load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [projectId, imageId, paletteId]);

  const reload = useCallback(() => {
    load();
  }, [load]);

  const toggleVisibility = useCallback((groupId: string) => {
    setVisibility((current) => ({ ...current, [groupId]: !current[groupId] }));
  }, []);

  const isolate = useCallback((groupId: string) => {
    setVisibility((current) => Object.fromEntries(Object.keys(current).map((key) => [key, key === groupId])));
  }, []);

  const showAll = useCallback(() => {
    setVisibility((current) => Object.fromEntries(Object.keys(current).map((key) => [key, true])));
  }, []);

  const selectGroup = useCallback((groupId: string | null) => {
    setSelectedGroupId(groupId);
  }, []);

  return {
    status,
    document,
    emptyReason,
    errorMessage,
    reload,
    visibility,
    toggleVisibility,
    isolate,
    showAll,
    selectedGroupId,
    selectGroup,
  };
}
