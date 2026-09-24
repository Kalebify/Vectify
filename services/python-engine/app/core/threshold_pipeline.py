"""Transformaciones puras de la etapa de threshold B/N (OpenCV): reducción a
un único canal de gris y umbral global determinista, con inversión opcional,
más el cálculo de métricas de porcentaje foreground/background. Ver spec.md
M1-S04, sección "Python/FastAPI".

Deliberadamente en un módulo propio (no agregado a app.core.pipeline ni a
PreprocessParams): conceptualmente el threshold es la etapa SIGUIENTE del
pipeline, que opera sobre el preview YA preprocesado -- no un parámetro más
del preprocesamiento continuo (grayscale/contraste/brillo/denoise). Ver
decisión de diseño documentada en el reporte del sprint.

Cada función es pura y determinista (mismo criterio que app.core.pipeline):
misma entrada + mismos parámetros -> siempre la misma salida, sin
aleatoriedad ni efectos secundarios.
"""

import cv2
import numpy as np


def to_grayscale_single_channel(image: np.ndarray) -> np.ndarray:
    """Reduce a un único canal de gris, requerido por cv2.threshold. Acepta
    tanto una imagen ya en un solo canal (passthrough) como una de 3 canales
    (color, o escala de grises replicada a BGR por app.core.pipeline.to_grayscale).
    """
    if image.ndim == 2:
        return image
    return cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)


def apply_threshold(gray: np.ndarray, value: int, invert: bool) -> np.ndarray:
    """Umbral global determinista vía cv2.threshold: THRESH_BINARY (píxeles
    > value -> 255, el resto -> 0), o THRESH_BINARY_INV si `invert` (píxeles
    <= value -> 255). Sin aleatoriedad ni estado: misma entrada + mismos
    parámetros siempre produce el mismo array byte a byte (ver spec.md,
    criterio de aceptación "resultado determinista").

    El modo adaptativo (cv2.adaptiveThreshold) queda deliberadamente fuera de
    este sprint -- ver "Decisiones de diseño" en el reporte del sprint.
    """
    threshold_type = cv2.THRESH_BINARY_INV if invert else cv2.THRESH_BINARY
    _, mask = cv2.threshold(gray, value, 255, threshold_type)
    return mask


def compute_threshold_metrics(mask: np.ndarray) -> dict[str, float]:
    """Porcentaje de píxeles foreground (valor 255 en la máscara binaria YA
    resultante, con la inversión ya aplicada por `apply_threshold`) vs.
    background (valor 0). Ver spec.md M1-S04: "Python calcula métricas de
    porcentaje foreground/background junto con la máscara". La clasificación
    de "casi vacía/casi llena" como advertencia vive del lado de Vectify.Api
    (ver Threshold/ThresholdService.cs) -- acá solo se calcula el porcentaje
    crudo.
    """
    total = int(mask.size)
    if total == 0:
        return {"foreground_percent": 0.0, "background_percent": 0.0}

    foreground = int(np.count_nonzero(mask == 255))
    foreground_percent = (foreground / total) * 100.0

    return {
        "foreground_percent": foreground_percent,
        "background_percent": 100.0 - foreground_percent,
    }
