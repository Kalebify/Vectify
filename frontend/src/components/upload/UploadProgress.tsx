interface UploadProgressProps {
  percent: number;
}

/** Barra de progreso de la subida en curso (0-100), con rol accesible nativo. */
export function UploadProgress({ percent }: UploadProgressProps) {
  return (
    <div className="upload-progress">
      <progress className="upload-progress__bar" value={percent} max={100} aria-label="Progreso de la carga" />
      <span className="upload-progress__label">{percent}%</span>
    </div>
  );
}
