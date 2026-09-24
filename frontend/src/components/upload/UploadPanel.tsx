import { Dropzone } from "./Dropzone";
import { FilePreview } from "./FilePreview";
import { UploadProgress } from "./UploadProgress";
import { ProjectCreatedCard } from "./ProjectCreatedCard";
import { useImageUpload } from "../../hooks/useImageUpload";

/**
 * Orquesta el flujo de "Usuario podrá" de spec.md: arrastrar/seleccionar PNG/JPG/WEBP,
 * ver nombre/tamaño/preview, cancelar o confirmar la carga, y recibir errores
 * comprensibles. Solo consume la Web API (nunca Python), vía src/api/projectsApi.ts.
 */
export function UploadPanel() {
  const { status, file, previewUrl, progress, errorMessage, result, selectFile, confirm, cancel, reset } =
    useImageUpload();

  if (status === "success" && result) {
    return <ProjectCreatedCard project={result} onUploadAnother={reset} />;
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
