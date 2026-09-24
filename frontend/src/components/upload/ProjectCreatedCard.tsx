import { getOriginalImageUrl } from "../../api/projectsApi";
import { formatBytes } from "../../lib/formatBytes";
import type { UploadImageResponse } from "../../types/upload";

interface ProjectCreatedCardProps {
  project: UploadImageResponse;
  onUploadAnother: () => void;
}

/** Pantalla de proyecto recién creado: confirma la carga y expone sus metadatos. */
export function ProjectCreatedCard({ project, onUploadAnother }: ProjectCreatedCardProps) {
  const originalUrl = getOriginalImageUrl(project.projectId, project.imageId);

  return (
    <div className="project-created" role="status">
      <img
        src={originalUrl}
        alt={`Original de ${project.filename}`}
        className="project-created__thumbnail"
        width={96}
        height={96}
        loading="lazy"
      />
      <div className="project-created__details">
        <p className="project-created__title">Proyecto creado</p>
        <dl className="service-card__details">
          <div>
            <dt>Archivo</dt>
            <dd>{project.filename}</dd>
          </div>
          <div>
            <dt>Tamaño</dt>
            <dd>{formatBytes(project.bytes)}</dd>
          </div>
          {project.width && project.height && (
            <div>
              <dt>Dimensiones</dt>
              <dd>
                {project.width} × {project.height} px
              </dd>
            </div>
          )}
          <div>
            <dt>ID de proyecto</dt>
            <dd>{project.projectId}</dd>
          </div>
        </dl>
        <a href={originalUrl} target="_blank" rel="noreferrer" className="project-created__link">
          Ver original
        </a>
        <button type="button" className="upload-actions__button" onClick={onUploadAnother}>
          Cargar otra imagen
        </button>
      </div>
    </div>
  );
}
