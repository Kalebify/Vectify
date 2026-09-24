interface ImageComparisonProps {
  originalUrl: string;
  originalAlt: string;
  originalWidth: number | null;
  originalHeight: number | null;
  previewUrl: string | null;
  previewAlt: string;
  previewWidth: number | null;
  previewHeight: number | null;
}

/**
 * Comparación lado a lado de original vs. procesado ("React: ... comparación
 * original/procesado" — spec.md M1-S03). Mientras no hay preview todavía
 * (primera carga) muestra un placeholder del mismo tamaño para no correr el
 * layout. Dimensiones explícitas en ambas imágenes: reservan espacio y evitan
 * layout shift mientras cargan.
 */
export function ImageComparison({
  originalUrl,
  originalAlt,
  originalWidth,
  originalHeight,
  previewUrl,
  previewAlt,
  previewWidth,
  previewHeight,
}: ImageComparisonProps) {
  return (
    <div className="preprocess-comparison">
      <figure className="preprocess-comparison__item">
        {originalWidth && originalHeight ? (
          <img src={originalUrl} alt={originalAlt} width={originalWidth} height={originalHeight} loading="lazy" />
        ) : (
          <img src={originalUrl} alt={originalAlt} loading="lazy" />
        )}
        <figcaption>Original</figcaption>
      </figure>

      <figure className="preprocess-comparison__item">
        {previewUrl ? (
          previewWidth && previewHeight ? (
            <img src={previewUrl} alt={previewAlt} width={previewWidth} height={previewHeight} loading="lazy" />
          ) : (
            <img src={previewUrl} alt={previewAlt} loading="lazy" />
          )
        ) : (
          <div
            className="preprocess-comparison__placeholder"
            style={{ width: previewWidth ?? 240, height: previewHeight ?? 180 }}
            role="img"
            aria-label="Generando preview…"
          />
        )}
        <figcaption>Preprocesada</figcaption>
      </figure>
    </div>
  );
}
