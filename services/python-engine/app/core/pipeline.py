"""Transformaciones puras del pipeline de preprocesamiento (OpenCV).

Cada función es pura y determinista: misma entrada (array/bytes) + mismos
parámetros -> siempre la misma salida, sin efectos secundarios (no tocan disco
ni red). La orquestación (leer bytes, aplicar la secuencia de pasos, calcular
métricas, codificar la salida) vive en app.services.preprocessing_service, que
es lo único que conoce el orden del pipeline y los settings.

Orden fijo del pipeline (definido acá, no en el caller, para que el resultado
sea reproducible sin importar cómo se invoquen las funciones): grayscale ->
contraste/brillo -> suavizado/denoise. Ver spec.md M1-S03, sección
"Python/FastAPI".
"""

import cv2
import numpy as np

from app.core.errors import CorruptImageError, DimensionsExceededError


def read_image_safely(data: bytes) -> np.ndarray:
    """Decodifica bytes de imagen de forma segura (lectura acotada al buffer
    recibido, sin escribir a disco). Nunca lanza una excepción no controlada de
    OpenCV/NumPy: cualquier fallo de decodificación se traduce a
    CorruptImageError.
    """
    if not data:
        raise CorruptImageError("El archivo está vacío.")

    try:
        array = np.frombuffer(data, dtype=np.uint8)
        image = cv2.imdecode(array, cv2.IMREAD_COLOR)
    except cv2.error as exc:  # pragma: no cover - OpenCV rara vez lanza acá en vez de devolver None
        raise CorruptImageError(f"No se pudo decodificar la imagen: {exc}") from exc

    if image is None:
        raise CorruptImageError(
            "No se pudo decodificar la imagen: el contenido está corrupto o el formato no es soportado."
        )

    return image


def check_dimensions(image: np.ndarray, max_width: int, max_height: int, max_pixels: int) -> None:
    """Valida que la imagen decodificada no exceda los límites configurados.
    Se ejecuta DESPUÉS de decodificar (la decodificación en sí ya acotó el
    trabajo al tamaño del archivo recibido) y ANTES de aplicar cualquier
    transformación, para no gastar tiempo de CPU en una imagen que se va a
    rechazar.
    """
    height, width = image.shape[:2]
    total_pixels = width * height

    if width > max_width or height > max_height or total_pixels > max_pixels:
        raise DimensionsExceededError(
            f"La imagen ({width}x{height}, {total_pixels} px) supera el límite permitido "
            f"({max_width}x{max_height}, {max_pixels} px totales)."
        )


def to_grayscale(image: np.ndarray, enabled: bool) -> np.ndarray:
    """Convierte a escala de grises cuando `enabled` es True. El resultado se
    replica a 3 canales BGR para que el resto del pipeline (contraste/brillo,
    denoise) pueda operar siempre sobre una imagen de 3 canales, sin ramas
    especiales más adelante.
    """
    if not enabled:
        return image

    gray = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)
    return cv2.cvtColor(gray, cv2.COLOR_GRAY2BGR)


def adjust_contrast_brightness(image: np.ndarray, contrast: float, brightness: int) -> np.ndarray:
    """Contraste (factor multiplicativo) + brillo/normalización (offset
    aditivo) en un solo paso lineal: out = clip(contrast * in + brightness, 0, 255).

    OJO: no se usa cv2.convertScaleAbs, porque esa función aplica valor
    absoluto DESPUÉS de alpha*in+beta y ANTES de saturar
    (dst = saturate(|alpha*in + beta|)), lo que produce resultados incorrectos
    para brillo negativo (ej. un píxel negro con brightness=-100 da 100 en vez
    de 0). En su lugar se calcula en float32 (ancho suficiente para no
    desbordar con los rangos de contraste/brillo soportados), se clippea a
    [0, 255] sin abs y se vuelve a uint8. Determinista: mismos
    contrast/brightness sobre la misma imagen siempre dan el mismo resultado
    byte a byte. Sin cambios, se evita una copia innecesaria.
    """
    if contrast == 1.0 and brightness == 0:
        return image

    scaled = image.astype(np.float32) * contrast + brightness
    clipped = np.clip(scaled, 0, 255)
    return clipped.astype(np.uint8)


def denoise(image: np.ndarray, strength: int) -> np.ndarray:
    """Suavizado/reducción de ruido determinista mediante un blur Gaussiano.
    strength=0 es no-op. Cada unidad de `strength` aumenta el kernel (siempre
    impar, 2*strength+1) y el sigma proporcionalmente. Se eligió GaussianBlur
    (en vez de fastNlMeansDenoising) porque es determinista, rápido y suficiente
    para el alcance de este sprint (preparar la imagen para tracing, no
    denoising fotográfico de alta fidelidad) — ver supuestos del reporte.
    """
    if strength <= 0:
        return image

    kernel_size = 2 * strength + 1
    sigma = strength * 0.5
    return cv2.GaussianBlur(image, (kernel_size, kernel_size), sigmaX=sigma, sigmaY=sigma)


def compute_metrics(image: np.ndarray) -> dict[str, float | int]:
    """Métricas básicas del resultado (sobre su versión en escala de grises,
    para que sean comparables sin importar si `grayscale` estaba activo):
    brillo medio, desvío estándar (proxy de contraste) y rango de valores.
    """
    gray = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY) if image.ndim == 3 else image
    mean, std_dev = cv2.meanStdDev(gray)

    return {
        "mean_brightness": float(mean[0][0]),
        "std_dev": float(std_dev[0][0]),
        "min_value": int(gray.min()),
        "max_value": int(gray.max()),
    }


def encode_png(image: np.ndarray) -> bytes:
    """Codifica el resultado como PNG (formato sin pérdida, para que la salida
    sea determinista pixel a pixel) con un nivel de compresión fijo — el nivel
    de compresión de zlib no afecta los píxeles decodificados, pero fijarlo
    además garantiza bytes de archivo idénticos ante la misma entrada.
    """
    success, buffer = cv2.imencode(".png", image, [cv2.IMWRITE_PNG_COMPRESSION, 3])
    if not success:
        raise CorruptImageError("No se pudo codificar la imagen procesada como PNG.")

    return buffer.tobytes()
