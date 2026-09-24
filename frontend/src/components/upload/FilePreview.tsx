import { formatBytes } from "../../lib/formatBytes";

interface FilePreviewProps {
  file: File;
  previewUrl: string;
}

/** Nombre, tamaño y preview de imagen del archivo elegido, antes o durante la carga. */
export function FilePreview({ file, previewUrl }: FilePreviewProps) {
  return (
    <div className="file-preview">
      <img
        src={previewUrl}
        alt={`Vista previa de ${file.name}`}
        className="file-preview__thumbnail"
        width={96}
        height={96}
      />
      <div className="file-preview__details">
        <p className="file-preview__name">{file.name}</p>
        <p className="file-preview__size">{formatBytes(file.size)}</p>
      </div>
    </div>
  );
}
