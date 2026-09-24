"""Utilidades para generar imágenes de prueba en memoria (golden images
pequeñas), sin depender de archivos externos — ver spec.md M1-S03, sección
"Pruebas": "Golden images pequeñas".
"""

import cv2
import numpy as np


def make_png_bytes(width: int, height: int, color: tuple[int, int, int] = (40, 80, 120)) -> bytes:
    """PNG determinista en memoria: un rectángulo sólido de `color` (BGR,
    convención OpenCV). Sirve para tests de contrato/reproducibilidad donde el
    contenido exacto de los píxeles no importa, solo que sea estable.
    """
    image = np.full((height, width, 3), color, dtype=np.uint8)
    success, buffer = cv2.imencode(".png", image)
    assert success
    return buffer.tobytes()


def make_checkerboard_png_bytes(size: int = 8) -> bytes:
    """PNG determinista en memoria con un patrón de tablero de ajedrez
    (blanco/negro), útil para probar que el denoise/blur realmente cambia los
    píxeles de forma predecible.
    """
    image = np.zeros((size, size, 3), dtype=np.uint8)
    image[::2, ::2] = (255, 255, 255)
    image[1::2, 1::2] = (255, 255, 255)
    success, buffer = cv2.imencode(".png", image)
    assert success
    return buffer.tobytes()


def make_rgba_png_bytes(
    width: int, height: int, color: tuple[int, int, int] = (40, 80, 120), alpha: int = 128
) -> bytes:
    """PNG con canal alfa (BGRA), para probar que el pipeline no falla con
    imágenes con transparencia -- read_image_safely decodifica con
    IMREAD_COLOR, que descarta el canal alfa (ver spec.md M1-S04, "Pruebas":
    "transparencias").
    """
    image = np.full((height, width, 4), (*color, alpha), dtype=np.uint8)
    success, buffer = cv2.imencode(".png", image)
    assert success
    return buffer.tobytes()


NOT_AN_IMAGE = b"esto no es una imagen"
