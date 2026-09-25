"""Tests de app.core.simplification_pipeline: Douglas-Peucker sobre SVGs de
ejemplo escritos a mano -- no dependen de VTracer ni de VectorizationService
(eso lo cubre test_simplification_service.py). Cubre los casos de prueba
obligatorios de spec.md M1-S07: curvas suaves, esquinas, agujeros, texto
trazado (múltiples subpaths pequeños) y tolerancias extremas.
"""

import math
import xml.etree.ElementTree as ET

from app.core.simplification_pipeline import simplify_svg_paths
from app.core.svg_processing import compute_svg_stats


def _node_count(svg: str) -> int:
    return compute_svg_stats(svg)["approx_node_count"]


def _paths(svg: str) -> list[str]:
    root = ET.fromstring(svg)
    return [el.attrib.get("d", "") for el in root.iter() if el.tag.rsplit("}", 1)[-1] == "path"]


def _make_smooth_curve_svg(points: int = 60, radius: float = 100.0) -> str:
    """Aproximación poligonal de un círculo (muchos puntos casi colineales
    entre sí) -- lo más cercano a una "curva suave" que puede emitir el motor
    actual (mode="polygon", solo M/L/Z, ver vector_engine.py)."""
    coords = []
    for i in range(points):
        angle = 2 * math.pi * i / points
        x = radius + radius * math.cos(angle)
        y = radius + radius * math.sin(angle)
        coords.append(f"{x:.3f},{y:.3f}")
    d = "M" + "L".join(coords) + "Z"
    return f'<svg xmlns="http://www.w3.org/2000/svg" width="200" height="200"><path d="{d}"/></svg>'


SHARP_CORNER_SVG = (
    '<svg xmlns="http://www.w3.org/2000/svg" width="100" height="100">'
    '<path d="M10,10 L90,15 L15,90 Z"/>'
    "</svg>"
)

RING_WITH_HOLE_SVG = (
    '<svg xmlns="http://www.w3.org/2000/svg" width="80" height="80">'
    '<path d="M10,10 L70,10 L70,70 L10,70 Z M30,30 L50,30 L50,50 L30,50 Z" fill="#000000"/>'
    "</svg>"
)

# "Texto trazado": varios glifos pequeños, cada uno un subpath cerrado
# independiente dentro del mismo <path> (mismo criterio que hierarchical=
# "stacked" de VTracer para agujeros, ver vector_engine.py).
TRACED_TEXT_SVG = (
    '<svg xmlns="http://www.w3.org/2000/svg" width="100" height="20">'
    '<path d="'
    "M2,2 L8,2 L8,18 L2,18 Z"
    "M12,2 L18,2 L18,18 L12,18 Z"
    "M22,2 L28,2 L28,18 L22,18 Z"
    '" fill="#000000"/>'
    "</svg>"
)

CURVE_COMMAND_SVG = (
    '<svg xmlns="http://www.w3.org/2000/svg" width="50" height="50">'
    '<path d="M10,10 C20,0 30,0 40,10 L40,40 L10,40 Z"/>'
    "</svg>"
)

# Bug 1 (regex de tokenización incompleto): "H"/"V" no eran reconocidos como
# límites de comando, así que sus argumentos se mezclaban con el comando
# anterior en vez de marcar el path como no soportado -- ver
# simplification_pipeline.py, _COMMAND_SPLIT_RE.
HV_COMMAND_SQUARE_SVG = (
    '<svg xmlns="http://www.w3.org/2000/svg" width="50" height="50">'
    '<path d="M10,10 H40 V40 H10 Z"/>'
    "</svg>"
)

# Bug 2 (comandos relativos en minúscula tratados como soportados): el resto
# del pipeline trata todas las coordenadas como absolutas, sin acumular
# offset -- ver simplification_pipeline.py, _SUPPORTED_COMMANDS.
LOWERCASE_RELATIVE_SQUARE_SVG = (
    '<svg xmlns="http://www.w3.org/2000/svg" width="50" height="50">'
    '<path d="m10,10 l30,0 l0,30 z"/>'
    "</svg>"
)


def test_simplify_reduces_node_count_on_smooth_curve():
    svg = _make_smooth_curve_svg()
    before = _node_count(svg)

    simplified = simplify_svg_paths(svg, epsilon_ratio=0.01)
    after = _node_count(simplified)

    assert after < before
    assert after >= 3  # sigue siendo un polígono válido


def test_simplify_keeps_result_as_well_formed_xml():
    svg = _make_smooth_curve_svg()

    simplified = simplify_svg_paths(svg, epsilon_ratio=0.01)

    root = ET.fromstring(simplified)  # no lanza -> XML bien formado
    assert root.tag.endswith("svg")


def test_simplify_keeps_closed_subpath_closed():
    svg = _make_smooth_curve_svg()

    simplified = simplify_svg_paths(svg, epsilon_ratio=0.02)

    d = _paths(simplified)[0]
    assert d.rstrip().endswith("Z")


def test_simplify_preserves_sharp_corners_at_low_tolerance():
    # Con una tolerancia chica, Douglas-Peucker no debería descartar ninguno
    # de los 3 vértices de un triángulo (todos definen la forma).
    before = _node_count(SHARP_CORNER_SVG)

    simplified = simplify_svg_paths(SHARP_CORNER_SVG, epsilon_ratio=0.001)

    assert _node_count(simplified) == before


def test_simplify_preserves_hole_topology_as_two_subpaths_in_one_path():
    simplified = simplify_svg_paths(RING_WITH_HOLE_SVG, epsilon_ratio=0.01)

    root = ET.fromstring(simplified)
    paths = [el for el in root.iter() if el.tag.rsplit("}", 1)[-1] == "path"]
    assert len(paths) == 1  # sigue siendo UN <path> con dos subpaths (outer + hole)

    d = paths[0].attrib["d"]
    assert d.count("M") == 2  # outer + hole, ninguno se perdió
    assert d.count("Z") == 2  # ambos siguen cerrados


def test_simplify_keeps_all_small_subpaths_of_traced_text():
    # "Texto trazado": múltiples subpaths chicos (glifos). Ya son mínimos (4
    # puntos cada uno): con cualquier tolerancia razonable no deberían perder
    # ningún subpath completo (eso rompería el diseño -- letras faltantes).
    simplified = simplify_svg_paths(TRACED_TEXT_SVG, epsilon_ratio=0.01)

    d = _paths(simplified)[0]
    assert d.count("M") == 3
    assert d.count("Z") == 3


def test_simplify_at_minimum_tolerance_does_not_increase_node_count():
    svg = _make_smooth_curve_svg()
    before = _node_count(svg)

    simplified = simplify_svg_paths(svg, epsilon_ratio=1e-6)

    assert _node_count(simplified) <= before


def test_simplify_at_extreme_tolerance_still_produces_a_valid_closed_path():
    # Tolerancia extrema (máxima permitida, 0.5): no debe romper el path (debe
    # seguir siendo un polígono cerrado válido, aunque muy degradado) -- ver
    # spec.md, criterios de aceptación: "path sigue siendo válido ... cerrado
    # si era cerrado".
    svg = _make_smooth_curve_svg()

    simplified = simplify_svg_paths(svg, epsilon_ratio=0.5)

    root = ET.fromstring(simplified)  # sigue siendo XML bien formado
    d = _paths(simplified)[0]
    assert d.rstrip().endswith("Z")
    assert _node_count(simplified) >= 3  # nunca colapsa a menos de un triángulo


def test_simplify_leaves_unsupported_curve_commands_untouched():
    # El motor actual (VtracerEngine, mode="polygon") nunca emite "C", pero si
    # apareciera (u otro comando no soportado), el path se deja intacto en vez
    # de arriesgar corromperlo -- ver docstring del módulo.
    simplified = simplify_svg_paths(CURVE_COMMAND_SVG, epsilon_ratio=0.1)

    assert _paths(simplified)[0] == _paths(CURVE_COMMAND_SVG)[0]


def test_simplify_leaves_h_and_v_commands_untouched():
    # Bug 1: el regex de tokenización no reconocía "H"/"V" como límites de
    # comando, así que un cuadrado válido se corrompía en una línea/triángulo
    # degenerado en vez de dejarse intacto. Ver docstring del módulo.
    simplified = simplify_svg_paths(HV_COMMAND_SQUARE_SVG, epsilon_ratio=0.5)

    assert _paths(simplified)[0] == _paths(HV_COMMAND_SQUARE_SVG)[0]


def test_simplify_leaves_lowercase_relative_commands_untouched():
    # Bug 2: "m"/"l"/"z" (relativos) se aceptaban como "soportados" pero el
    # pipeline trata todas las coordenadas como absolutas (no acumula
    # offset), produciendo coordenadas erróneas. Deben dejarse intactos igual
    # que cualquier otro comando no soportado. Ver docstring del módulo.
    simplified = simplify_svg_paths(LOWERCASE_RELATIVE_SQUARE_SVG, epsilon_ratio=0.01)

    assert _paths(simplified)[0] == _paths(LOWERCASE_RELATIVE_SQUARE_SVG)[0]


def test_simplify_is_deterministic():
    svg = _make_smooth_curve_svg()

    first = simplify_svg_paths(svg, epsilon_ratio=0.02)
    second = simplify_svg_paths(svg, epsilon_ratio=0.02)

    assert first == second


def test_simplify_on_empty_svg_returns_it_unchanged():
    empty_svg = '<svg xmlns="http://www.w3.org/2000/svg" width="10" height="10"></svg>'

    simplified = simplify_svg_paths(empty_svg, epsilon_ratio=0.1)

    assert simplified == empty_svg
