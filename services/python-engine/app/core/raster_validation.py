"""M2.1-S03: "Validación raster-vs-vector" -- lo más nuevo de esta tarjeta
(ver spec.md, "Criterios de aceptación ampliados"). Después de vectorizar la
máscara de UNA capa (M2-S02), rasteriza el SVG resultante de vuelta a una
máscara binaria del MISMO tamaño que la máscara de origen y la compara
contra ella, con dos métricas relativas independientes:

1. `own_mismatch_ratio`: cuán fiel es la geometría vectorial a SU PROPIA
   máscara de origen (píxeles en los que el SVG rasterizado y la máscara
   original difieren, como fracción del área de foreground de la máscara
   original) -- captura errores de trazado/redondeo "normales" (antialiasing
   en los bordes, cuantización a coordenadas enteras de VTracer).

2. `contamination_ratio`: cuán CONTAMINADA queda el área vectorizada de esta
   capa con píxeles que en realidad pertenecen a la máscara de OTRO color
   (fracción del área rasterizada de ESTA capa que cae dentro de la unión de
   las demás máscaras de la paleta) -- la señal "crucial" pedida por
   spec.md: "evitar que regiones de otro color aparezcan dentro del layer
   seleccionado". Deliberadamente una métrica DISTINTA de `own_mismatch_ratio`
   (no "cuántos píxeles coinciden en general"): un error de alineación
   symmetric respecto a la propia máscara no dice nada sobre si el error
   invade el territorio de otro color en particular.

Ambas relativas (no absolutas en píxeles) -- mismo estilo que el resto de
las tolerancias del motor (CheckParams, ComponentAnalysisParams): así
escalan con el tamaño/densidad de la capa en vez de ser un número fijo de
píxeles. Ver app.core.config.Settings.raster_validation_own_mismatch_tolerance/
raster_validation_contamination_tolerance para los valores por defecto (con
evidencia empírica documentada en el reporte del sprint) y la decisión de
"advertir, no bloquear".

Reutiliza EXACTAMENTE la misma infraestructura de parseo de subpaths que
app.core.component_analysis (app.core.svg_path_parsing.collect_document_subpaths)
-- mismo criterio de "M/L/Z absolutos, transform resoluble" y mismos
subpaths en coordenadas ABSOLUTAS, sin duplicar el tokenizer.
"""

import math
import xml.etree.ElementTree as ET

import cv2
import numpy as np

from app.core.svg_path_parsing import collect_document_subpaths

# Un subpath de menos de 3 puntos no delimita ningún área rellenable.
_MIN_POINTS_FOR_FILL = 3

# `cv2.fillPoly` rasteriza un polígono incluyendo su borde LEJANO (columna/fila
# máxima) además del cercano -- confirmado empíricamente (ver reporte del
# sprint, "Rasterización: sesgo sistemático de +1 fila/columna"): un cuadrado
# de 30x30 vectorizado con vértices exactos en la grilla de píxeles (M0,0
# L30,0 L30,30 L0,30, mismo criterio de VtracerEngine mode="polygon") se
# rasteriza de vuelta como 31x31 (961 en vez de 900 píxeles) si se rellena
# directo a la resolución final -- un sesgo sistemático que por sí solo ya
# supera cualquier tolerancia razonable en formas chicas (una capa de 4x4
# píxeles mide >50% de "mismatch" solo por este artefacto, no por ningún
# error real de trazado). Se corrige rasterizando a una resolución
# SUPERMUESTREADA (factor `scale`, cada píxel final se subdivide en
# `scale`x`scale` subpíxeles) y reduciendo con `cv2.INTER_AREA` (promedio de
# cobertura) + umbral al 50% -- el resultado converge al criterio correcto
# "el píxeld entra si su CENTRO cae dentro del polígono", eliminando el sesgo
# de borde inclusive de `cv2.fillPoly` (verificado: mismatch 0 en el caso de
# arriba con `scale=8`, ver reporte del sprint).
_MIN_SUPERSAMPLE_SCALE = 1
_MAX_SUPERSAMPLE_SCALE = 8

# Presupuesto de píxeles supermuestreados (ancho*alto*scale^2 del recorte al
# bounding box de la capa, NO del lienzo completo -- ver más abajo): acota el
# costo/memoria de la corrección de arriba para capas grandes (ej. un fondo
# que cubre casi todo el lienzo) sin imponerle el mismo factor de escala x8
# que necesita una capa chica -- 4_000_000 es del mismo orden que
# max_svg_output_bytes/max_image_pixels ya usados como límites de este motor
# (ver app.core.config.Settings), suficiente para llegar a scale=8 en capas
# de hasta ~250x250 px y degradar gradualmente el factor de escala (nunca por
# debajo de 1, nunca sin rasterizar del todo) en capas más grandes -- donde
# el sesgo relativo de +1 fila/columna ya es naturalmente chico frente al
# área total (confirmado empíricamente: un cuadrado de 200x120 sin ninguna
# corrección ya mide solo ~1.7% de mismatch, contra ~6.8% en uno de 30x30).
_SUPERSAMPLE_PIXEL_BUDGET = 4_000_000


def rasterize_svg_mask(svg_text: str, width: int, height: int) -> np.ndarray:
    """Rasteriza los subpaths ANALIZABLES (ver `collect_document_subpaths`,
    misma limitación ya documentada/aceptada por component_analysis: `<path>`
    con comandos/transform no soportados se ignoran) de un SVG a una máscara
    binaria (0/255) de exactamente `width`x`height` -- las mismas dimensiones
    que la máscara raster de origen (M2-S02 garantiza que ninguna capa se
    recorta a su propio bounding box, así que el sistema de coordenadas ya
    coincide sin ninguna normalización adicional).

    Composición por XOR (no por relleno acumulativo simple): cada subpath se
    rellena en un lienzo temporal propio y se combina con XOR sobre el
    acumulado -- para la topología "stacked" que emite VtracerEngine (anillos
    simples anidados, sin auto-intersección, sentido de recorrido opuesto
    para agujeros, ver vector_engine.py), esto es EQUIVALENTE al resultado
    real de fill-rule nonzero con anidamiento simple: cada nivel de anidado
    alterna adentro/afuera exactamente igual que la paridad par/impar que ya
    usa app.core.component_analysis para decidir agujero vs. sólido -- sin
    duplicar ese cálculo de profundidad/contención (privado a ese módulo),
    solo para rasterizar.

    Solo se rasteriza (a resolución supermuestreada, ver constantes de
    arriba) el recorte del RECTÁNGULO delimitador de todos los subpaths de
    esta capa (expandido 1px de margen, recortado al lienzo) -- el resto del
    lienzo es 0/background por construcción, así que no hace falta
    supermuestrear área que de entrada no tiene geometría.
    """
    root = ET.fromstring(svg_text)
    subpaths, _ = collect_document_subpaths(root)

    mask = np.zeros((height, width), dtype=np.uint8)
    fillable = [sp for sp in subpaths if len(sp["points"]) >= _MIN_POINTS_FOR_FILL]
    if not fillable:
        return mask

    xs = [x for sp in fillable for x, _ in sp["points"]]
    ys = [y for sp in fillable for _, y in sp["points"]]
    min_x = max(0, int(math.floor(min(xs))) - 1)
    min_y = max(0, int(math.floor(min(ys))) - 1)
    max_x = min(width, int(math.ceil(max(xs))) + 1)
    max_y = min(height, int(math.ceil(max(ys))) + 1)
    bbox_width = max(1, max_x - min_x)
    bbox_height = max(1, max_y - min_y)

    scale = int(math.floor(math.sqrt(_SUPERSAMPLE_PIXEL_BUDGET / (bbox_width * bbox_height))))
    scale = max(_MIN_SUPERSAMPLE_SCALE, min(_MAX_SUPERSAMPLE_SCALE, scale))

    big_width, big_height = bbox_width * scale, bbox_height * scale
    supersampled = np.zeros((big_height, big_width), dtype=np.uint8)
    for subpath in fillable:
        polygon = np.array(
            [[round((x - min_x) * scale), round((y - min_y) * scale)] for x, y in subpath["points"]],
            dtype=np.int32,
        )
        layer = np.zeros((big_height, big_width), dtype=np.uint8)
        cv2.fillPoly(layer, [polygon], 255)
        supersampled = cv2.bitwise_xor(supersampled, layer)

    downsampled = cv2.resize(supersampled, (bbox_width, bbox_height), interpolation=cv2.INTER_AREA)
    _, bbox_mask = cv2.threshold(downsampled, 127, 255, cv2.THRESH_BINARY)
    mask[min_y:max_y, min_x:max_x] = bbox_mask

    return mask


class RasterValidationOutcome:
    """Resultado puro de comparar la geometría reconstruida de UNA capa
    contra su máscara de origen y contra las máscaras de las DEMÁS capas de
    la misma paleta. Ver `compare_layer_raster`."""

    def __init__(
        self,
        own_mismatch_ratio: float,
        own_mismatch_tolerance: float,
        contamination_ratio: float,
        contamination_tolerance: float,
    ) -> None:
        self.own_mismatch_ratio = own_mismatch_ratio
        self.own_mismatch_tolerance = own_mismatch_tolerance
        self.contamination_ratio = contamination_ratio
        self.contamination_tolerance = contamination_tolerance

    @property
    def own_mismatch_within_tolerance(self) -> bool:
        return self.own_mismatch_ratio <= self.own_mismatch_tolerance

    @property
    def contamination_within_tolerance(self) -> bool:
        return self.contamination_ratio <= self.contamination_tolerance

    @property
    def warnings(self) -> list[str]:
        """Mensajes legibles para logging -- nunca bloquean la generación de
        la capa (ver spec.md, "Ambigüedades detectadas": "advertir, no
        bloquear", decisión documentada en el reporte del sprint)."""
        messages: list[str] = []
        if not self.own_mismatch_within_tolerance:
            messages.append(
                f"La geometría vectorial difiere de su máscara de origen en "
                f"{self.own_mismatch_ratio:.4%} de su área de foreground "
                f"(tolerancia: {self.own_mismatch_tolerance:.4%})."
            )
        if not self.contamination_within_tolerance:
            messages.append(
                f"El área vectorizada de esta capa contiene {self.contamination_ratio:.4%} "
                f"de píxeles que pertenecen a la máscara de OTRO color "
                f"(tolerancia: {self.contamination_tolerance:.4%}) -- posible contaminación cruzada."
            )
        return messages

    def to_dict(self) -> dict:
        return {
            "own_mismatch_ratio": self.own_mismatch_ratio,
            "own_mismatch_tolerance": self.own_mismatch_tolerance,
            "own_mismatch_within_tolerance": self.own_mismatch_within_tolerance,
            "contamination_ratio": self.contamination_ratio,
            "contamination_tolerance": self.contamination_tolerance,
            "contamination_within_tolerance": self.contamination_within_tolerance,
            "warnings": self.warnings,
        }


def compare_layer_raster(
    reconstructed_mask: np.ndarray,
    own_mask: np.ndarray,
    other_masks_union: np.ndarray,
    own_mismatch_tolerance: float,
    contamination_tolerance: float,
) -> RasterValidationOutcome:
    """Compara la máscara RECONSTRUIDA (rasterizada de vuelta desde el SVG,
    ver `rasterize_svg_mask`) contra (a) la máscara ORIGINAL de esta misma
    capa -- fidelidad propia -- y (b) la UNIÓN de las máscaras originales de
    TODAS las demás capas de la paleta -- contaminación cruzada. Las tres
    máscaras deben tener exactamente las mismas dimensiones (garantizado por
    el caller: mismo lienzo sin recortar para toda la paleta, M2-S02).

    `own_mismatch_ratio` = píxeles donde reconstructed_mask y own_mask
    difieren (XOR), relativo al área de foreground de `own_mask` (si la
    máscara original está vacía -- no debería ocurrir, M2-S02 ya rechaza
    máscaras vacías antes de llegar acá -- se usa 1 como denominador para
    evitar división por cero sin lanzar).

    `contamination_ratio` = píxeles donde reconstructed_mask es foreground Y
    `other_masks_union` también es foreground, relativo al área de
    foreground de `reconstructed_mask` (mismo criterio de "relativo al propio
    resultado" -- si la capa reconstruida está vacía, 0% de contaminación por
    definición, no hay área que contaminar).
    """
    own_foreground = int(np.count_nonzero(own_mask))
    reconstructed_foreground = int(np.count_nonzero(reconstructed_mask))

    mismatch_pixels = int(np.count_nonzero(cv2.bitwise_xor(reconstructed_mask, own_mask)))
    own_mismatch_ratio = mismatch_pixels / max(own_foreground, 1)

    if reconstructed_foreground == 0:
        contamination_ratio = 0.0
    else:
        contamination_pixels = int(np.count_nonzero(cv2.bitwise_and(reconstructed_mask, other_masks_union)))
        contamination_ratio = contamination_pixels / reconstructed_foreground

    return RasterValidationOutcome(
        own_mismatch_ratio=own_mismatch_ratio,
        own_mismatch_tolerance=own_mismatch_tolerance,
        contamination_ratio=contamination_ratio,
        contamination_tolerance=contamination_tolerance,
    )
