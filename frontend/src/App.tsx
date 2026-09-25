import { useState } from "react";
import { getOriginalImageUrl } from "./api/projectsApi";
import { getPreviewImageUrl } from "./api/preprocessApi";
import { getSimplificationSvgUrl } from "./api/simplifyApi";
import { getVectorSvgUrl } from "./api/vectorizeApi";
import { ServiceCard } from "./components/ServiceCard";
import type { StatusTone } from "./components/StatusPill";
import { CheckPanel, type CheckSourceOption } from "./components/check/CheckPanel";
import { DimensionPanel, type DimensionSourceOption } from "./components/dimensions/DimensionPanel";
import { ExportPanel, type ExportSourceOption } from "./components/export/ExportPanel";
import { PreprocessPanel } from "./components/preprocess/PreprocessPanel";
import { SimplifyPanel } from "./components/simplify/SimplifyPanel";
import { ThresholdPanel } from "./components/threshold/ThresholdPanel";
import { UploadPanel } from "./components/upload/UploadPanel";
import { VectorizePanel } from "./components/vectorize/VectorizePanel";
import { useSystemHealth } from "./hooks/useSystemHealth";
import type { DimensionResponse } from "./types/dimension";
import type { PreprocessResponse } from "./types/preprocess";
import type { SimplifyResponse } from "./types/simplify";
import type { PythonStatus } from "./types/system";
import type { ThresholdResponse } from "./types/threshold";
import type { UploadImageResponse } from "./types/upload";
import type { VectorizeResponse } from "./types/vectorize";
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

/**
 * El Laser Checker (M1-S08) acepta como fuente cualquiera de los dos SVG ya
 * generados del pipeline: el vector original (M1-S05) o -- si el usuario ya
 * aplicó una simplificación (M1-S07) -- esa versión simplificada. Se
 * ofrecen ambos como opciones en vez de reemplazar uno por otro: el spec no
 * define cuál "debería" analizarse, y un usuario puede querer comparar los
 * issues antes/después de simplificar.
 */
function buildCheckSources(
  project: UploadImageResponse,
  vector: VectorizeResponse,
  simplification: SimplifyResponse | null,
): CheckSourceOption[] {
  const sources: CheckSourceOption[] = [
    {
      kind: "vector",
      id: vector.vectorId,
      label: "Vector actual",
      svgUrl: getVectorSvgUrl(project.projectId, project.imageId, vector.vectorId),
      width: vector.width,
      height: vector.height,
    },
  ];

  if (simplification) {
    sources.push({
      kind: "simplification",
      id: simplification.simplificationId,
      label: "Última simplificación",
      svgUrl: getSimplificationSvgUrl(project.projectId, project.imageId, simplification.simplificationId),
      width: simplification.width,
      height: simplification.height,
    });
  }

  return sources;
}

/**
 * M1-S09 opera sobre el mismo par de fuentes que el Laser Checker (M1-S08):
 * el vector original o -- si ya existe -- la última simplificación aplicada
 * (ver spec.md, "no está definido si opera sobre VectorVersion o
 * SimplificationVersion" -- se aceptan ambas, mismo criterio que
 * buildCheckSources).
 */
function buildDimensionSources(
  vector: VectorizeResponse,
  simplification: SimplifyResponse | null,
): DimensionSourceOption[] {
  const sources: DimensionSourceOption[] = [
    { kind: "vector", id: vector.vectorId, label: "Vector actual", widthPx: vector.width, heightPx: vector.height },
  ];

  if (simplification) {
    sources.push({
      kind: "simplification",
      id: simplification.simplificationId,
      label: "Última simplificación",
      widthPx: simplification.width,
      heightPx: simplification.height,
    });
  }

  return sources;
}

/**
 * M1-S10 cierra el pipeline dejando elegir CUALQUIERA de los tres artefactos
 * SVG ya persistidos: el vector original, la última simplificación (si se
 * aplicó) y la última versión con dimensiones físicas en mm (si se aplicó) --
 * extiende a un tercer valor el mismo patrón dual de buildCheckSources/
 * buildDimensionSources. checkSourceKind/checkSourceId de la fuente
 * "dimension" apuntan al vector/simplificación de origen (nunca al propio
 * dimensionId): el Laser Checker (M1-S08) no tiene un sourceKind "dimension"
 * porque Dimensioning nunca toca los `d` de los paths -- la geometría es
 * idéntica a la de su fuente.
 */
function buildExportSources(
  vector: VectorizeResponse,
  simplification: SimplifyResponse | null,
  dimension: DimensionResponse | null,
): ExportSourceOption[] {
  const sources: ExportSourceOption[] = [
    {
      kind: "vector",
      id: vector.vectorId,
      label: "Vector actual",
      version: vector.version,
      widthPx: vector.width,
      heightPx: vector.height,
      checkSourceKind: "vector",
      checkSourceId: vector.vectorId,
    },
  ];

  if (simplification) {
    sources.push({
      kind: "simplification",
      id: simplification.simplificationId,
      label: "Última simplificación",
      version: simplification.version,
      widthPx: simplification.width,
      heightPx: simplification.height,
      checkSourceKind: "simplification",
      checkSourceId: simplification.simplificationId,
    });
  }

  if (dimension) {
    sources.push({
      kind: "dimension",
      id: dimension.dimensionId,
      label: "Última versión con dimensiones físicas",
      version: dimension.version,
      widthPx: dimension.sourceWidthPx,
      heightPx: dimension.sourceHeightPx,
      widthMm: dimension.widthMm,
      heightMm: dimension.heightMm,
      checkSourceKind: dimension.sourceKind,
      checkSourceId: dimension.sourceId,
    });
  }

  return sources;
}

function App() {
  const { status, response, errorMessage, lastCheckedAt } = useSystemHealth();
  const [activeProject, setActiveProject] = useState<UploadImageResponse | null>(null);
  const [readyPreview, setReadyPreview] = useState<PreprocessResponse | null>(null);
  const [readyMask, setReadyMask] = useState<ThresholdResponse | null>(null);
  const [readyVector, setReadyVector] = useState<VectorizeResponse | null>(null);
  const [readySimplification, setReadySimplification] = useState<SimplifyResponse | null>(null);
  const [readyDimension, setReadyDimension] = useState<DimensionResponse | null>(null);

  const handleProjectCreated = (project: UploadImageResponse | null) => {
    setReadyPreview(null);
    setReadyMask(null);
    setReadyVector(null);
    setReadySimplification(null);
    setReadyDimension(null);
    setActiveProject(project);
  };

  const handlePreviewReady = (preview: PreprocessResponse) => {
    setReadyMask(null);
    setReadyVector(null);
    setReadySimplification(null);
    setReadyDimension(null);
    setReadyPreview(preview);
  };

  const handleMaskReady = (mask: ThresholdResponse) => {
    setReadyVector(null);
    setReadySimplification(null);
    setReadyDimension(null);
    setReadyMask(mask);
  };

  const handleVectorReady = (vector: VectorizeResponse) => {
    setReadySimplification(null);
    setReadyDimension(null);
    setReadyVector(vector);
  };

  // Una simplificación nueva invalida cualquier DimensionVersion vigente que
  // se haya derivado de la simplificación ANTERIOR (aunque el SVG dimensionado
  // en sí siga existiendo intacto en el storage, ya no sería "la última
  // versión" con la que tiene sentido seguir trabajando en el resto del
  // pipeline) -- mismo criterio de invalidación en cascada que handleVectorReady.
  const handleSimplificationApplied = (simplification: SimplifyResponse) => {
    setReadyDimension(null);
    setReadySimplification(simplification);
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
              onMaskReady={handleMaskReady}
            />
          </section>
        )}

        {activeProject && readyMask && (
          <section aria-labelledby="vectorize-heading" className="vectorize-section">
            <h2 id="vectorize-heading">Vectorización</h2>
            <p className="upload-section__hint">
              Pulsá "Vectorizar" para convertir la máscara binaria en un SVG. El motor de
              trazado corre del lado del servidor; la máscara nunca se modifica. Una vez listo,
              podés compararlo contra el original con zoom, pan y ajuste a pantalla.
            </p>
            <VectorizePanel
              key={`${activeProject.projectId}-${activeProject.imageId}-${readyMask.maskId}`}
              projectId={activeProject.projectId}
              imageId={activeProject.imageId}
              fileName={activeProject.filename}
              sourceMaskId={readyMask.maskId}
              originalUrl={getOriginalImageUrl(activeProject.projectId, activeProject.imageId)}
              originalWidth={activeProject.width}
              originalHeight={activeProject.height}
              onVectorReady={handleVectorReady}
            />
          </section>
        )}

        {activeProject && readyVector && (
          <section aria-labelledby="simplify-heading" className="simplify-section">
            <h2 id="simplify-heading">Simplificación de nodos</h2>
            <p className="upload-section__hint">
              El trazado puede generar miles de nodos. Elegí una tolerancia, revisá el preview
              (nodos antes/después y % de reducción) y aplicá solo si el resultado te convence.
              Cancelar no persiste nada: el SVG ya vectorizado nunca se sobrescribe.
            </p>
            <SimplifyPanel
              key={`${activeProject.projectId}-${activeProject.imageId}-${readyVector.vectorId}`}
              projectId={activeProject.projectId}
              imageId={activeProject.imageId}
              fileName={activeProject.filename}
              sourceVectorId={readyVector.vectorId}
              currentVectorUrl={getVectorSvgUrl(activeProject.projectId, activeProject.imageId, readyVector.vectorId)}
              currentVectorWidth={readyVector.width}
              currentVectorHeight={readyVector.height}
              onSimplificationApplied={handleSimplificationApplied}
            />
          </section>
        )}

        {activeProject && readyVector && (
          <section aria-labelledby="check-heading" className="check-section">
            <h2 id="check-heading">Paths abiertos y duplicados</h2>
            <p className="upload-section__hint">
              Analizá el SVG en busca de geometría que puede producir cortes láser inesperados:
              paths que deberían estar cerrados y no lo están, y segmentos duplicados o
              casi-duplicados. El análisis es de solo lectura: nunca modifica el SVG, y se ejecuta
              solo cuando lo pedís.
            </p>
            <CheckPanel
              key={`${activeProject.projectId}-${activeProject.imageId}-${readyVector.vectorId}-${readySimplification?.simplificationId ?? "none"}`}
              projectId={activeProject.projectId}
              imageId={activeProject.imageId}
              fileName={activeProject.filename}
              sources={buildCheckSources(activeProject, readyVector, readySimplification)}
            />
          </section>
        )}

        {activeProject && readyVector && (
          <section aria-labelledby="dimension-heading" className="dimension-section">
            <h2 id="dimension-heading">Dimensiones físicas</h2>
            <p className="upload-section__hint">
              Definí el ancho o el alto en milímetros para fabricación con láser. Con la proporción
              bloqueada (default) el otro valor se calcula automáticamente; desbloqueada, podés
              definir ambos de forma independiente (esto deforma el diseño). El SVG resultante
              conserva su tamaño físico al reabrirlo en cualquier visor.
            </p>
            <DimensionPanel
              key={`${activeProject.projectId}-${activeProject.imageId}-${readyVector.vectorId}-${readySimplification?.simplificationId ?? "none"}`}
              projectId={activeProject.projectId}
              imageId={activeProject.imageId}
              sources={buildDimensionSources(readyVector, readySimplification)}
              onDimensionApplied={setReadyDimension}
            />
          </section>
        )}

        {activeProject && readyVector && (
          <section aria-labelledby="export-heading" className="export-section">
            <h2 id="export-heading">Exportar SVG</h2>
            <p className="upload-section__hint">
              Elegí qué versión descargar (vector, simplificación o dimensión física), revisá su
              tamaño y los issues del Laser Checker, y descargá el archivo. El Laser Checker es
              solo informativo: nunca impide la descarga, y el archivo exportado es exactamente el
              mismo SVG ya generado por esa etapa, sin modificar su geometría.
            </p>
            <ExportPanel
              key={`${activeProject.projectId}-${activeProject.imageId}-${readyVector.vectorId}-${readySimplification?.simplificationId ?? "none"}-${readyDimension?.dimensionId ?? "none"}`}
              projectId={activeProject.projectId}
              imageId={activeProject.imageId}
              sources={buildExportSources(readyVector, readySimplification, readyDimension)}
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
        <p>Vectify · M1-S10 · Exportación SVG</p>
      </footer>
    </>
  );
}

export default App;
