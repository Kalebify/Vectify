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

from app.core.errors import CorruptImageError

# Un píxel se considera "sin contenido" (BACKGROUND) cuando su canal alpha es
# <= este umbral. Deliberadamente 0 (transparencia TOTAL), no un valor
# intermedio: alpha parcial (ej. anti-aliasing en los bordes de un logo) sigue
# siendo una decisión del umbral B/N sobre su color compuesto, no de alpha --
# solo la ausencia total de contenido se fuerza a background sin mirar el
# color RGB que OpenCV compuso "debajo".
ALPHA_BACKGROUND_THRESHOLD = 0


def read_image_with_alpha(data: bytes) -> np.ndarray:
    """Decodifica bytes de imagen preservando el canal alpha si existe, a
    diferencia de app.core.pipeline.read_image_safely (usado por
    Preprocessing y Vectorization, sin cambios), que decodifica con
    cv2.IMREAD_COLOR y por lo tanto DESCARTA cualquier canal alpha -- un
    píxel transparente terminaba tratándose según el color RGB que tuviera
    "debajo" en vez de como ausencia de contenido. Usado exclusivamente por
    el flujo de threshold (ThresholdingService), donde la transparencia debe
    tener significado semántico propio -- ver spec.md M1-S04, "transparencias"
    como caso de prueba explícito.

    Devuelve el array tal cual lo decodifica OpenCV: 2 dimensiones (gris) o 3
    dimensiones con 3 canales (BGR, sin alpha) o 4 canales (BGRA, con alpha) --
    la mayoría de las imágenes no tienen alpha, por lo que el caso común sigue
    siendo BGR de 3 canales.
    """
    if not data:
        raise CorruptImageError("El archivo está vacío.")

    try:
        array = np.frombuffer(data, dtype=np.uint8)
        image = cv2.imdecode(array, cv2.IMREAD_UNCHANGED)
    except cv2.error as exc:  # pragma: no cover - OpenCV rara vez lanza acá en vez de devolver None
        raise CorruptImageError(f"No se pudo decodificar la imagen: {exc}") from exc

    if image is None:
        raise CorruptImageError(
            "No se pudo decodificar la imagen: el contenido está corrupto o el formato no es soportado."
        )

    return image


def split_alpha_channel(image: np.ndarray) -> tuple[np.ndarray, np.ndarray | None]:
    """Separa el canal alpha si la imagen decodificada tiene 4 canales
    (BGRA), devolviendo (imagen_bgr, alpha). Si no tiene alpha (el caso
    común: gris de 2 dimensiones o BGR de 3 canales), es un passthrough que
    devuelve (imagen, None) sin modificar nada.
    """
    if image.ndim == 3 and image.shape[2] == 4:
        return image[:, :, :3], image[:, :, 3]
    return image, None


def apply_alpha_as_background(
    mask: np.ndarray, alpha: np.ndarray | None, alpha_threshold: int = ALPHA_BACKGROUND_THRESHOLD
) -> np.ndarray:
    """Fuerza a BACKGROUND (0) cualquier píxel cuyo canal alpha original sea
    <= `alpha_threshold`, sin importar el valor que le haya asignado
    `apply_threshold` según su color RGB compuesto -- ver
    ALPHA_BACKGROUND_THRESHOLD para la justificación del umbral. Si `alpha`
    es None (imagen sin canal alpha, el caso común), es un passthrough que
    devuelve `mask` sin modificar.
    """
    if alpha is None:
        return mask

    result = mask.copy()
    result[alpha <= alpha_threshold] = 0
    return result


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
