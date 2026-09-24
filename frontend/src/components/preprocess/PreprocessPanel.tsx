import { useEffect } from "react";
import { getPreviewImageUrl } from "../../api/preprocessApi";
import { getOriginalImageUrl } from "../../api/projectsApi";
import { usePreprocess } from "../../hooks/usePreprocess";
import type { PreprocessResponse } from "../../types/preprocess";
import { ImageComparison } from "./ImageComparison";
import { ParameterControls } from "./ParameterControls";

interface PreprocessPanelProps {
  projectId: string;
  imageId: string;
  fileName: string;
  originalWidth: number | null;
  originalHeight: number | null;
  /**
   * Notifica al padre cada vez que hay un preview nuevo listo (M1-S04: el
   * threshold es la etapa siguiente del mismo pipeline y opera sobre este
   * preview, nunca sobre el original). Opcional para no romper otros usos
   * de este panel que no necesiten encadenar la etapa siguiente.
   */
  onPreviewReady?: (preview: PreprocessResponse) => void;
}

/**
 * Orquesta el flujo de "Usuario podrá" de spec.md M1-S03: ajustar escala de
 * grises/contraste/brillo/reducción de ruido con sliders + debounce, ver
 * loading y la comparación original/procesado, y restablecer valores. Solo
 * consume la Web API (nunca Python directamente).
 */
export function PreprocessPanel({
  projectId,
  imageId,
  fileName,
  originalWidth,
  originalHeight,
  onPreviewReady,
}: PreprocessPanelProps) {
  const {
    params,
    status,
    preview,
    errorMessage,
    isAtDefaults,
    setGrayscale,
    setContrast,
    setBrightness,
    setDenoise,
    reset,
  } = usePreprocess(projectId, imageId);

  useEffect(() => {
    if (status === "ready" && preview) {
      onPreviewReady?.(preview);
    }
  }, [status, preview, onPreviewReady]);

  const originalUrl = getOriginalImageUrl(projectId, imageId);
  const previewUrl = preview ? getPreviewImageUrl(projectId, imageId, preview.previewId) : null;

  return (
    <div className="preprocess-panel">
      <ParameterControls
        params={params}
        isLoading={status === "loading"}
        isAtDefaults={isAtDefaults}
        onGrayscaleChange={setGrayscale}
        onContrastChange={setContrast}
        onBrightnessChange={setBrightness}
        onDenoiseChange={setDenoise}
        onReset={reset}
      />

      {status === "error" && errorMessage && (
        <p className="upload-panel__error" role="alert">
          {errorMessage}
        </p>
      )}

      <ImageComparison
        originalUrl={originalUrl}
        originalAlt={`Original de ${fileName}`}
        originalWidth={originalWidth}
        originalHeight={originalHeight}
        previewUrl={previewUrl}
        previewAlt={`Preview preprocesado de ${fileName}`}
        previewWidth={preview?.width ?? originalWidth}
        previewHeight={preview?.height ?? originalHeight}
      />

      {preview && (
        <dl className="service-card__details preprocess-panel__metrics">
          <div>
            <dt>Versión de configuración</dt>
            <dd>{preview.version}</dd>
          </div>
          <div>
            <dt>Brillo medio</dt>
            <dd>{preview.metrics.meanBrightness.toFixed(1)}</dd>
          </div>
          <div>
            <dt>Desvío estándar (contraste)</dt>
            <dd>{preview.metrics.stdDev.toFixed(1)}</dd>
          </div>
        </dl>
      )}
    </div>
  );
}
