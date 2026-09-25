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


def make_half_transparent_rgba_png_bytes(
    width: int, height: int, color: tuple[int, int, int] = (200, 200, 200)
) -> bytes:
    """PNG BGRA determinista con dos mitades del MISMO color RGB claro (un
    color que, sin transparencia, cae del lado foreground con cualquier
    umbral B/N razonable): la mitad izquierda totalmente opaca (alpha=255) y
    la mitad derecha totalmente transparente (alpha=0). Usado para probar la
    semántica real de la transparencia en threshold (ver spec.md M1-S04,
    "transparencias"): la mitad transparente debe quedar como background en
    la máscara resultante pese a tener el mismo color "debajo" que la mitad
    opaca -- no alcanza con "no crashea", como hacían los tests anteriores.
    """
    image = np.full((height, width, 4), (*color, 255), dtype=np.uint8)
    half = width // 2
    image[:, half:, 3] = 0
    success, buffer = cv2.imencode(".png", image)
    assert success
    return buffer.tobytes()


def make_mask_png_bytes(width: int, height: int, invert: bool = False) -> bytes:
    """Máscara B/N determinista (formato de salida de Threshold, M1-S04): todo
    negro (background, valor 0) por defecto -- útil como base de "máscara
    vacía" para tests de M1-S05.
    """
    value = 255 if invert else 0
    image = np.full((height, width), value, dtype=np.uint8)
    success, buffer = cv2.imencode(".png", image)
    assert success
    return buffer.tobytes()


def make_square_mask_png_bytes(size: int = 60, square: int = 30) -> bytes:
    """Máscara B/N con un cuadrado blanco (foreground=255) centrado sobre
    fondo negro -- silueta simple, ver spec.md M1-S05, "Pruebas": "logos/
    siluetas simples".
    """
    image = np.zeros((size, size), dtype=np.uint8)
    offset = (size - square) // 2
    image[offset : offset + square, offset : offset + square] = 255
    success, buffer = cv2.imencode(".png", image)
    assert success
    return buffer.tobytes()


def make_ring_mask_png_bytes(size: int = 80, outer_radius: int = 30, inner_radius: int = 12) -> bytes:
    """Máscara B/N con un anillo (círculo blanco con un agujero negro
    concéntrico) -- topología con un agujero interno, ver spec.md M1-S05,
    "Pruebas": "formas con agujeros internos (topología con paths anidados/
    fill-rule)".
    """
    image = np.zeros((size, size), dtype=np.uint8)
    center = (size // 2, size // 2)
    cv2.circle(image, center, outer_radius, 255, -1)
    cv2.circle(image, center, inner_radius, 0, -1)
    success, buffer = cv2.imencode(".png", image)
    assert success
    return buffer.tobytes()


def make_gradient_png_bytes(width: int = 32, height: int = 32) -> bytes:
    """PNG BGR determinista con un degradé diagonal de muchísimos colores
    únicos (uno distinto por cada combinación de fila/columna, dentro del
    rango 0-255) -- usado para el caso de prueba "muchos colores" de la
    paleta de colores (M2-S01, ver spec.md, "Pruebas")."""
    xs = np.linspace(0, 255, width, dtype=np.uint8)
    ys = np.linspace(0, 255, height, dtype=np.uint8)
    blue = np.tile(xs, (height, 1))
    green = np.tile(ys.reshape(-1, 1), (1, width))
    red = (blue.astype(np.int32) + green.astype(np.int32)) % 256
    image = np.stack([blue, green, red.astype(np.uint8)], axis=-1)
    success, buffer = cv2.imencode(".png", image)
    assert success
    return buffer.tobytes()


def make_antialiased_edge_png_bytes(width: int = 20, height: int = 10) -> bytes:
    """PNG BGR determinista con dos bloques de color sólido separados por
    una franja de columnas con un gradiente de mezcla lineal entre ambos
    colores (antialiasing sintético) -- ver spec.md M2-S01, "Pruebas":
    "anti-aliasing (bordes con gradiente de color)"."""
    left_color = np.array([30, 30, 200], dtype=np.float64)  # BGR
    right_color = np.array([200, 30, 30], dtype=np.float64)
    image = np.zeros((height, width, 3), dtype=np.uint8)
    edge_width = max(2, width // 4)
    edge_start = (width - edge_width) // 2

    for x in range(width):
        if x < edge_start:
            color = left_color
        elif x >= edge_start + edge_width:
            color = right_color
        else:
            t = (x - edge_start) / (edge_width - 1)
            color = left_color * (1 - t) + right_color * t
        image[:, x] = color.astype(np.uint8)

    success, buffer = cv2.imencode(".png", image)
    assert success
    return buffer.tobytes()


def make_shadow_png_bytes(width: int = 12, height: int = 12) -> bytes:
    """PNG BGR determinista con el MISMO color lógico en dos luminosidades
    distintas (mitad "a la luz", mitad "en sombra") -- ver spec.md M2-S01,
    "Pruebas": "sombras (variaciones de luminosidad del mismo color
    lógico)". Ambas mitades deben tender a fusionarse en un clustering con
    tolerancia amplia, y a quedar separadas con tolerancia estricta."""
    base = np.array([180, 90, 40], dtype=np.uint8)  # BGR
    shadow = (base.astype(np.float64) * 0.55).astype(np.uint8)
    image = np.zeros((height, width, 3), dtype=np.uint8)
    half = width // 2
    image[:, :half] = base
    image[:, half:] = shadow
    success, buffer = cv2.imencode(".png", image)
    assert success
    return buffer.tobytes()


def make_near_identical_colors_png_bytes(width: int = 8, height: int = 8) -> bytes:
    """PNG BGR determinista con dos colores casi idénticos (difieren en solo
    unas pocas unidades por canal) en cada mitad -- ver spec.md M2-S01,
    "Pruebas": "colores casi iguales (deben tender a fusionarse según
    tolerancia)"."""
    color_a = np.array([100, 150, 200], dtype=np.uint8)
    color_b = np.array([103, 152, 202], dtype=np.uint8)
    image = np.zeros((height, width, 3), dtype=np.uint8)
    half = width // 2
    image[:, :half] = color_a
    image[:, half:] = color_b
    success, buffer = cv2.imencode(".png", image)
    assert success
    return buffer.tobytes()


def make_solid_colors_png_bytes(width: int = 12, height: int = 12) -> bytes:
    """PNG BGR determinista con TRES bloques de colores sólidos bien
    separados entre sí (rojo, verde, azul puros) -- ver spec.md M2-S01,
    "Pruebas": "colores sólidos (pocos colores, separación clara)"."""
    image = np.zeros((height, width, 3), dtype=np.uint8)
    third = width // 3
    image[:, :third] = (0, 0, 255)  # BGR: rojo puro
    image[:, third : 2 * third] = (0, 255, 0)  # verde puro
    image[:, 2 * third :] = (255, 0, 0)  # azul puro
    success, buffer = cv2.imencode(".png", image)
    assert success
    return buffer.tobytes()


NOT_AN_IMAGE = b"esto no es una imagen"
