import { getThresholdMaskImageUrl } from "../../api/thresholdApi";
import { useThreshold } from "../../hooks/useThreshold";
import { MaskComparison } from "./MaskComparison";
import { ThresholdControls } from "./ThresholdControls";

interface ThresholdPanelProps {
  projectId: string;
  imageId: string;
  fileName: string;
  sourcePreviewId: string;
  sourcePreviewUrl: string;
  sourceWidth: number | null;
  sourceHeight: number | null;
}

/**
 * Orquesta el flujo de "Usuario podrá" de spec.md M1-S04: ajustar
 * umbral/inversión con debounce, ver loading y la comparación
 * preprocesada/máscara, la advertencia cuando la máscara queda casi vacía o
 * casi llena, y restablecer valores. Solo consume la Web API (nunca Python
 * directamente). Opera sobre el previewId ya preprocesado (M1-S03): es la
 * etapa siguiente del mismo pipeline.
 */
export function ThresholdPanel({
  projectId,
  imageId,
  fileName,
  sourcePreviewId,
  sourcePreviewUrl,
  sourceWidth,
  sourceHeight,
}: ThresholdPanelProps) {
  const { params, status, mask, errorMessage, isAtDefaults, setValue, setInvert, reset } = useThreshold(
    projectId,
    imageId,
    sourcePreviewId,
  );

  const maskUrl = mask ? getThresholdMaskImageUrl(projectId, imageId, mask.maskId) : null;

  return (
    <div className="threshold-panel">
      <ThresholdControls
        params={params}
        isLoading={status === "loading"}
        isAtDefaults={isAtDefaults}
        onValueChange={setValue}
        onInvertChange={setInvert}
        onReset={reset}
      />

      {status === "error" && errorMessage && (
        <p className="upload-panel__error" role="alert">
          {errorMessage}
        </p>
      )}

      {mask?.metrics.warningCode && (
        <p className="threshold-panel__warning" role="alert">
          {mask.metrics.warningMessage}
        </p>
      )}

      <MaskComparison
        sourceUrl={sourcePreviewUrl}
        sourceAlt={`Preprocesada de ${fileName}`}
        sourceWidth={sourceWidth}
        sourceHeight={sourceHeight}
        maskUrl={maskUrl}
        maskAlt={`Máscara binaria de ${fileName}`}
        maskWidth={mask?.width ?? sourceWidth}
        maskHeight={mask?.height ?? sourceHeight}
      />

      {mask && (
        <dl className="service-card__details threshold-panel__metrics">
          <div>
            <dt>Versión de la máscara</dt>
            <dd>{mask.version}</dd>
          </div>
          <div>
            <dt>Foreground</dt>
            <dd>{mask.metrics.foregroundPercent.toFixed(1)}%</dd>
          </div>
          <div>
            <dt>Background</dt>
            <dd>{mask.metrics.backgroundPercent.toFixed(1)}%</dd>
          </div>
        </dl>
      )}
    </div>
  );
}
