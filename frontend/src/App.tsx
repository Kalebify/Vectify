import { useState } from "react";
import { getPreviewImageUrl } from "./api/preprocessApi";
import { getThresholdMaskImageUrl } from "./api/thresholdApi";
import { ServiceCard } from "./components/ServiceCard";
import type { StatusTone } from "./components/StatusPill";
import { PreprocessPanel } from "./components/preprocess/PreprocessPanel";
import { ThresholdPanel } from "./components/threshold/ThresholdPanel";
import { UploadPanel } from "./components/upload/UploadPanel";
import { VectorizePanel } from "./components/vectorize/VectorizePanel";
import { useSystemHealth } from "./hooks/useSystemHealth";
import type { PreprocessResponse } from "./types/preprocess";
import type { PythonStatus } from "./types/system";
import type { ThresholdResponse } from "./types/threshold";
import type { UploadImageResponse } from "./types/upload";
import "./App.css";

const PYTHON_STATUS_LABEL: Record<PythonStatus, string> = {
  online: "En línea",
  unavailable: "No disponible",
  timeout: "Tiempo de espera agotado",
  invalid_response: "Respuesta inválida",
  error: "Error",
};

const PYTHON_STATUS_TONE: Record<PythonStatus, StatusTone> = {
  online: "ok",
  unavailable: "error",
  timeout: "warn",
  invalid_response: "warn",
  error: "error",
};

const BANNER_COPY: Record<"loading" | "online" | "degraded" | "error", string> = {
  loading: "Consultando el estado del sistema…",
  online: "Todos los servicios están en línea.",
  degraded: "La Web API está en línea, pero el motor Python presenta problemas.",
  error: "No se pudo contactar a la Web API.",
};

function App() {
  const { status, response, errorMessage, lastCheckedAt } = useSystemHealth();
  const [activeProject, setActiveProject] = useState<UploadImageResponse | null>(null);
  const [readyPreview, setReadyPreview] = useState<PreprocessResponse | null>(null);
  const [readyMask, setReadyMask] = useState<ThresholdResponse | null>(null);

  const handleProjectCreated = (project: UploadImageResponse | null) => {
    setReadyPreview(null);
    setReadyMask(null);
    setActiveProject(project);
  };

  const handlePreviewReady = (preview: PreprocessResponse) => {
    setReadyMask(null);
    setReadyPreview(preview);
  };

  const apiTone: StatusTone =
    status === "loading" ? "neutral" : status === "error" ? "error" : "ok";
  const apiLabel =
    status === "loading" ? "Consultando…" : status === "error" ? "Sin conexión" : "En línea";

  const pythonTone: StatusTone =
    status === "loading" || status === "error"
      ? "neutral"
      : PYTHON_STATUS_TONE[response!.python.status];
  const pythonLabel =
    status === "loading"
      ? "Consultando…"
      : status === "error"
        ? "Desconocido"
        : PYTHON_STATUS_LABEL[response!.python.status];

  return (
    <>
      <header className="app-header">
        <h1>Vectify</h1>
        <p className="app-header__subtitle">
          Vectorizá tus imágenes: cargá un original y verificá el estado del sistema.
        </p>
      </header>

      <main>
        <section aria-labelledby="upload-heading" className="upload-section">
          <h2 id="upload-heading">Nuevo proyecto</h2>
          <p className="upload-section__hint">
            Arrastrá o seleccioná una imagen para crear un proyecto a partir de ella. El
            original se guarda tal cual: el preprocesamiento trabaja sobre una copia.
          </p>
          <UploadPanel onProjectCreated={handleProjectCreated} />
        </section>

        {activeProject && (
          <section aria-labelledby="preprocess-heading" className="preprocess-section">
            <h2 id="preprocess-heading">Preprocesamiento</h2>
            <p className="upload-section__hint">
              Ajustá escala de grises, contraste, brillo y reducción de ruido para preparar la
              imagen antes de vectorizarla. El original nunca se modifica.
            </p>
            <PreprocessPanel
              key={`${activeProject.projectId}-${activeProject.imageId}`}
              projectId={activeProject.projectId}
              imageId={activeProject.imageId}
              fileName={activeProject.filename}
              originalWidth={activeProject.width}
              originalHeight={activeProject.height}
              onPreviewReady={handlePreviewReady}
            />
          </section>
        )}

        {activeProject && readyPreview && (
          <section aria-labelledby="threshold-heading" className="threshold-section">
            <h2 id="threshold-heading">Threshold blanco y negro</h2>
            <p className="upload-section__hint">
              Ajustá el umbral y la inversión para convertir el preview preprocesado en una
              máscara binaria apta para vectorizar. El preview preprocesado nunca se modifica.
            </p>
            <ThresholdPanel
              key={`${activeProject.projectId}-${activeProject.imageId}-${readyPreview.previewId}`}
              projectId={activeProject.projectId}
              imageId={activeProject.imageId}
              fileName={activeProject.filename}
              sourcePreviewId={readyPreview.previewId}
              sourcePreviewUrl={getPreviewImageUrl(activeProject.projectId, activeProject.imageId, readyPreview.previewId)}
              sourceWidth={readyPreview.width}
              sourceHeight={readyPreview.height}
              onMaskReady={setReadyMask}
            />
          </section>
        )}

        {activeProject && readyMask && (
          <section aria-labelledby="vectorize-heading" className="vectorize-section">
            <h2 id="vectorize-heading">Vectorización</h2>
            <p className="upload-section__hint">
              Pulsá "Vectorizar" para convertir la máscara binaria en un SVG. El motor de
              trazado corre del lado del servidor; la máscara nunca se modifica.
            </p>
            <VectorizePanel
              key={`${activeProject.projectId}-${activeProject.imageId}-${readyMask.maskId}`}
              projectId={activeProject.projectId}
              imageId={activeProject.imageId}
              fileName={activeProject.filename}
              sourceMaskId={readyMask.maskId}
              sourceMaskUrl={getThresholdMaskImageUrl(activeProject.projectId, activeProject.imageId, readyMask.maskId)}
              sourceWidth={readyMask.width}
              sourceHeight={readyMask.height}
            />
          </section>
        )}

        <section aria-labelledby="diagnostics-heading">
          <h2 id="diagnostics-heading">Diagnóstico del sistema</h2>

          <div aria-label="Resumen general" className={`status-banner status-banner--${status}`}>
            <p>{BANNER_COPY[status]}</p>
          </div>

          <div aria-label="Estado de los servicios" className="service-grid">
            <ServiceCard title="Web API (ASP.NET Core)" tone={apiTone} statusLabel={apiLabel}>
              {status === "error" ? (
                <p className="service-card__message">
                  {errorMessage ?? "No se pudo establecer conexión con la Web API."}
                </p>
              ) : status === "loading" ? (
                <p className="service-card__message">Esperando la respuesta de la Web API.</p>
              ) : (
                <p className="service-card__message">
                  La Web API respondió correctamente a la última consulta de salud.
                </p>
              )}
            </ServiceCard>

            <ServiceCard title="Motor Python (FastAPI)" tone={pythonTone} statusLabel={pythonLabel}>
              {status === "loading" || status === "error" ? (
                <p className="service-card__message">
                  El estado del motor Python depende de la Web API; todavía no hay datos.
                </p>
              ) : (
                <dl className="service-card__details">
                  <div>
                    <dt>Servicio</dt>
                    <dd>{response!.python.service ?? "—"}</dd>
                  </div>
                  <div>
                    <dt>Versión</dt>
                    <dd>{response!.python.version ?? "—"}</dd>
                  </div>
                  {response!.python.message && (
                    <div>
                      <dt>Detalle</dt>
                      <dd>{response!.python.message}</dd>
                    </div>
                  )}
                </dl>
              )}
            </ServiceCard>
          </div>

          <p className="last-checked">
            {lastCheckedAt
              ? `Última verificación: ${lastCheckedAt.toLocaleTimeString()}`
              : "Aún no se realizó ninguna verificación."}
          </p>
        </section>
      </main>

      <footer className="app-footer">
        <p>Vectify · M1-S05 · Vectorización raster → SVG</p>
      </footer>
    </>
  );
}

export default App;
