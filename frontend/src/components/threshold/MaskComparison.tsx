interface MaskComparisonProps {
  sourceUrl: string;
  sourceAlt: string;
  sourceWidth: number | null;
  sourceHeight: number | null;
  maskUrl: string | null;
  maskAlt: string;
  maskWidth: number | null;
  maskHeight: number | null;
}

/**
 * Comparación lado a lado del preview preprocesado (entrada de esta etapa)
 * vs. la máscara binaria resultante ("React: ... comparación" -- spec.md
 * M1-S04). Mismo patrón que preprocess/ImageComparison.tsx (M1-S03), aplicado
 * a esta etapa del pipeline. Mientras no hay máscara todavía (primera carga)
 * muestra un placeholder del mismo tamaño para no correr el layout.
 * Dimensiones explícitas en ambas imágenes: reservan espacio y evitan layout
 * shift mientras cargan.
 */
export function MaskComparison({
  sourceUrl,
  sourceAlt,
  sourceWidth,
  sourceHeight,
  maskUrl,
  maskAlt,
  maskWidth,
  maskHeight,
}: MaskComparisonProps) {
  return (
    <div className="threshold-comparison">
      <figure className="threshold-comparison__item">
        {sourceWidth && sourceHeight ? (
          <img src={sourceUrl} alt={sourceAlt} width={sourceWidth} height={sourceHeight} loading="lazy" />
        ) : (
          <img src={sourceUrl} alt={sourceAlt} loading="lazy" />
        )}
        <figcaption>Preprocesada</figcaption>
      </figure>

      <figure className="threshold-comparison__item">
        {maskUrl ? (
          maskWidth && maskHeight ? (
            <img src={maskUrl} alt={maskAlt} width={maskWidth} height={maskHeight} loading="lazy" />
          ) : (
            <img src={maskUrl} alt={maskAlt} loading="lazy" />
          )
        ) : (
          <div
            className="threshold-comparison__placeholder"
            style={{ width: maskWidth ?? 240, height: maskHeight ?? 180 }}
            role="img"
            aria-label="Generando máscara…"
          />
        )}
        <figcaption>Máscara</figcaption>
      </figure>
    </div>
  );
}
