import { useEffect } from "react";
import { Dropzone } from "./Dropzone";
import { FilePreview } from "./FilePreview";
import { UploadProgress } from "./UploadProgress";
import { ProjectCreatedCard } from "./ProjectCreatedCard";
import { useImageUpload } from "../../hooks/useImageUpload";
import type { UploadImageResponse } from "../../types/upload";

interface UploadPanelProps {
  /**
   * Notificado con el proyecto recién creado (para que el padre pueda mostrar
   * el panel de preprocesamiento de M1-S03), o `null` cuando el usuario elige
   * "Cargar otra imagen" y vuelve a empezar.
   */
  onProjectCreated?: (project: UploadImageResponse | null) => void;
}

/**
 * Orquesta el flujo de "Usuario podrá" de spec.md: arrastrar/seleccionar PNG/JPG/WEBP,
 * ver nombre/tamaño/preview, cancelar o confirmar la carga, y recibir errores
 * comprensibles. Solo consume la Web API (nunca Python), vía src/api/projectsApi.ts.
 */
export function UploadPanel({ onProjectCreated }: UploadPanelProps) {
  const { status, file, previewUrl, progress, errorMessage, result, selectFile, confirm, cancel, reset } =
    useImageUpload();

  useEffect(() => {
    if (status === "success" && result) {
      onProjectCreated?.(result);
    }
  }, [status, result, onProjectCreated]);

  const handleUploadAnother = () => {
    onProjectCreated?.(null);
    reset();
  };

  if (status === "success" && result) {
    return <ProjectCreatedCard project={result} onUploadAnother={handleUploadAnother} />;
  }

  return (
    <div className="upload-panel">
      {status === "idle" && <Dropzone onFileSelected={selectFile} />}

      {status !== "idle" && file && previewUrl && (
        <div className="upload-panel__working">
          <FilePreview file={file} previewUrl={previewUrl} />

          {status === "uploading" && <UploadProgress percent={progress} />}

          {(status === "invalid" || status === "error") && errorMessage && (
            <p className="upload-panel__error" role="alert">
              {errorMessage}
            </p>
          )}

          <div className="upload-actions">
            {status === "selected" && (
              <>
                <button type="button" className="upload-actions__button" onClick={cancel}>
                  Cancelar
                </button>
                <button
                  type="button"
                  className="upload-actions__button upload-actions__button--primary"
                  onClick={confirm}
                >
                  Confirmar carga
                </button>
              </>
            )}

            {status === "uploading" && (
              <button type="button" className="upload-actions__button" onClick={cancel}>
                Cancelar
              </button>
            )}

            {status === "invalid" && (
              <button type="button" className="upload-actions__button" onClick={cancel}>
                Elegir otra imagen
              </button>
            )}

            {status === "error" && (
              <>
                <button type="button" className="upload-actions__button" onClick={cancel}>
                  Elegir otra imagen
                </button>
                <button
                  type="button"
                  className="upload-actions__button upload-actions__button--primary"
                  onClick={confirm}
                >
                  Reintentar
                </button>
              </>
            )}
          </div>
        </div>
      )}
    </div>
  );
}
