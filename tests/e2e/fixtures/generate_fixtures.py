"""Genera el dataset de fixtures E2E de M1-S11 (ver spec.md, sección
"Dataset": "logo, silueta, texto trazado, diseño con agujeros, ruido y caso
problemático con paths/duplicados").

Seis PNG raster, generados programáticamente con OpenCV + NumPy -- las
mismas dos librerías que ya son dependencias de producción del motor Python
(ver services/python-engine/requirements.txt); deliberadamente NO se agrega
Pillow ni ninguna otra librería nueva solo para generar fixtures de test.
Mismo criterio de "PNG determinista en memoria" que
services/python-engine/tests/support.py, pero acá los PNG se escriben a
disco bajo tests/e2e/fixtures/ porque este dataset se sube de verdad por
HTTP (multipart/form-data) en tests/e2e/full_pipeline_e2e_test.mjs -- no son
golden images para tests unitarios in-memory.

Convención compartida por los 6 fixtures: forma(s) en NEGRO sobre fondo
BLANCO (misma convención visual que un logo/silueta escaneado). El pipeline
de threshold (M1-S04) trata los píxeles CLAROS como foreground por defecto
(value=128, invert=false) -- así que el E2E que consume este dataset llama
al endpoint de threshold con invert=true, para que la forma oscura (no el
fondo claro) quede como foreground de la máscara. Ver docstring de cada
función para el razonamiento geométrico específico de cada fixture.

Uso (desde la raíz del repo, con el entorno virtual de services/python-engine
ya con sus dependencias instaladas -- cv2/numpy son las mismas de
requirements.txt, no hace falta instalar nada adicional):

    services/python-engine/.venv/Scripts/python.exe tests/e2e/fixtures/generate_fixtures.py

Determinista: correr este script dos veces produce bytes idénticos byte a
byte, incluido el fixture de "ruido" (usa una semilla fija de NumPy).
"""

from pathlib import Path

import cv2
import numpy as np

FIXTURES_DIR = Path(__file__).resolve().parent

WHITE = 255
BLACK = 0


def _save(name: str, image: np.ndarray) -> Path:
    path = FIXTURES_DIR / name
    ok = cv2.imwrite(str(path), image)
    assert ok, f"No se pudo escribir {path}"
    return path


def make_logo(size: int = 240) -> np.ndarray:
    """Logo: formas geométricas simples, un solo color, alto contraste --
    un anillo exterior + una estrella de 5 puntas concéntrica, ambos en
    negro sobre blanco (ver spec.md, "Dataset": "logo (formas simples,
    pocos colores)"). Ambas formas están conectadas (la estrella toca el
    anillo), así que vectoriza como una topología simple sin agujeros
    reales -- el fixture de agujeros es make_holes(), no este.
    """
    image = np.full((size, size), WHITE, dtype=np.uint8)
    center = (size // 2, size // 2)
    cv2.circle(image, center, size // 3, BLACK, thickness=size // 30)

    points = []
    outer_r, inner_r = size * 0.28, size * 0.12
    for i in range(10):
        angle = -np.pi / 2 + i * np.pi / 5
        r = outer_r if i % 2 == 0 else inner_r
        x = center[0] + r * np.cos(angle)
        y = center[1] + r * np.sin(angle)
        points.append((x, y))
    cv2.fillPoly(image, [np.array(points, dtype=np.int32)], BLACK)
    return image


def make_silhouette(size: int = 240) -> np.ndarray:
    """Silueta: un contorno cerrado simple y orgánico (ver spec.md,
    "Dataset": "silueta (contorno cerrado simple)") -- un blob asimétrico
    formado por tres círculos solapados y suavizado con blur + threshold, en
    vez de un círculo/rectángulo perfecto, para que el contorno resultante
    tenga curvas irregulares (más representativo de una silueta real,
    ej. una hoja/mancha) sin dejar de ser una única región cerrada.
    """
    canvas = np.full((size, size), WHITE, dtype=np.uint8)
    blob = np.zeros((size, size), dtype=np.uint8)
    circles = [
        (int(size * 0.42), int(size * 0.55), int(size * 0.30)),
        (int(size * 0.62), int(size * 0.40), int(size * 0.22)),
        (int(size * 0.55), int(size * 0.68), int(size * 0.20)),
    ]
    for cx, cy, r in circles:
        cv2.circle(blob, (cx, cy), r, WHITE, -1)
    blob = cv2.GaussianBlur(blob, (21, 21), 0)
    _, blob = cv2.threshold(blob, 90, 255, cv2.THRESH_BINARY)
    canvas[blob == WHITE] = BLACK
    return canvas


def make_text(width: int = 360, height: int = 120) -> np.ndarray:
    """Texto trazado: varios caracteres renderizados como imagen, para que
    al vectorizar genere múltiples subpaths pequeños -- mismo concepto que
    "texto trazado" ya usado en los tests unitarios de M1-S07 (ver
    services/python-engine/tests/test_simplification_pipeline.py), pero acá
    como PNG raster real en vez de un SVG sintético.
    """
    image = np.full((height, width), WHITE, dtype=np.uint8)
    cv2.putText(
        image, "VECTIFY", (12, 82), cv2.FONT_HERSHEY_SIMPLEX, 1.8, BLACK, thickness=4, lineType=cv2.LINE_8
    )
    return image


def make_holes(size: int = 240) -> np.ndarray:
    """Diseño con agujeros: una dona (anillo) -- círculo negro relleno con
    un círculo blanco concéntrico más chico "perforándolo" -- topología con
    un agujero real (ver spec.md, "Dataset": "diseño con agujeros, ej. una
    dona/letra 'O'/'A'"). Verificado empíricamente contra VTracer
    (mode=polygon, hierarchical=stacked): produce un único <path> con dos
    subpaths de sentido de recorrido opuesto (contorno exterior + agujero
    interior), exactamente la topología que M1-S05/M1-S07 ya ejercitan en
    sus tests unitarios (ver services/python-engine/tests/support.py,
    make_ring_mask_png_bytes) -- acá se genera el equivalente pero como
    imagen de "diseño" (negro sobre blanco) en vez de máscara B/N directa.
    """
    image = np.full((size, size), WHITE, dtype=np.uint8)
    center = (size // 2, size // 2)
    cv2.circle(image, center, int(size * 0.38), BLACK, -1)
    cv2.circle(image, center, int(size * 0.16), WHITE, -1)
    return image


def make_noise(size: int = 200, seed: int = 42) -> np.ndarray:
    """Ruido: una forma simple (círculo) sobre fondo, con ruido gaussiano
    fuerte agregado a TODA la imagen -- para que un threshold ingenuo (sin
    denoise) produzca máscaras llenas de artefactos/motas, y ejercite de
    verdad el pipeline de denoise (M1-S03) en vez de ser trivial de
    threshold-ear directamente. Semilla de NumPy fija (`seed`) para que el
    resultado sea reproducible byte a byte entre corridas.
    """
    rng = np.random.default_rng(seed)
    image = np.full((size, size), 235, dtype=np.float64)
    cv2.circle(image, (size // 2, size // 2), size // 3, 55, -1)
    noise = rng.normal(loc=0.0, scale=42.0, size=image.shape)
    noisy = np.clip(image + noise, 0, 255).astype(np.uint8)
    return noisy


def make_problematic(size: int = 240) -> np.ndarray:
    """Caso problemático (histórico) / fixture de regresión del fix M1-S11:
    dos círculos negros IDÉNTICOS (mismo radio), en posiciones distintas y
    bien separadas del lienzo, sobre fondo blanco.

    Verificado empíricamente contra el motor real (VtracerEngine, ver
    services/python-engine/app/core/vector_engine.py): al vectorizar dos
    formas congruentes mutuamente disjuntas, VTracer emite un <path> por
    cada una, cada uno con su propio `transform="translate(tx,ty)"` pero
    coordenadas `d` LOCALES relativas a un origen propio -- si las formas
    son congruentes, esas coordenadas `d` locales resultan BYTE A BYTE
    idénticas entre ambos <path>, y solo difiere el `transform`.

    Historia: hasta M1-S11, `app.core.path_checker` (M1-S08) comparaba los
    puntos de cada subpath ignorando `transform` por completo, así que este
    par de círculos congruentes-pero-separados disparaba de forma
    determinista un `duplicate_path` con `exact=true` -- un FALSO POSITIVO
    real (dos agujeros/elementos repetidos legítimos en posiciones distintas
    NO son un duplicado). Corregido en M1-S11 (ver
    services/python-engine/app/core/path_checker.py): ahora se resuelve
    `transform="translate(...)"` antes de comparar, así que este MISMO
    fixture, sin cambios, sirve como prueba de regresión E2E del fix --
    `duplicateGroupCount` debe dar 0 (ver
    tests/e2e/full_pipeline_e2e_test.mjs).

    Nota sobre por qué este fixture NO se rediseñó para disparar un
    duplicado LEGÍTIMO (dos formas en la MISMA posición real): verificado
    empíricamente (ver reporte de M1-S11) que es geométricamente imposible
    -- dos regiones rellenas del mismo color que ocupan literalmente la
    misma posición real en un raster plano se funden en UNA sola región al
    trazar (probado: dibujar el mismo círculo dos veces en el mismo lugar
    produce un único `<path>`, nunca dos). Un duplicado "legítimo" (misma
    posición real, dos elementos separados) solo puede existir en un SVG
    autoría manual, no en la salida de VtracerEngine sobre un PNG plano --
    ese escenario positivo está cubierto de forma exhaustiva a nivel unitario
    en services/python-engine/tests/test_path_checker.py, no en este dataset
    raster.
    """
    image = np.full((size, size), WHITE, dtype=np.uint8)
    radius = int(size * 0.14)
    cv2.circle(image, (int(size * 0.28), int(size * 0.28)), radius, BLACK, -1)
    cv2.circle(image, (int(size * 0.72), int(size * 0.72)), radius, BLACK, -1)
    return image


def main() -> None:
    _save("logo.png", make_logo())
    _save("silhouette.png", make_silhouette())
    _save("text.png", make_text())
    _save("holes.png", make_holes())
    _save("noise.png", make_noise())
    _save("problematic.png", make_problematic())
    print(f"OK: 6 fixtures generados en {FIXTURES_DIR}")


if __name__ == "__main__":
    main()
