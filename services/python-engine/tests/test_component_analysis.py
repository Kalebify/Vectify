"""Tests de app.core.component_analysis: detección de componentes físicos
independientes sobre SVGs de ejemplo escritos a mano -- no dependen de
VTracer ni de ComponentAnalysisService (eso lo cubre
test_component_analysis_service.py). Cubre los casos de prueba obligatorios
de spec.md M2-S03: islas separadas, agujeros internos, piezas tocándose,
tolerancias (casos límite justo debajo/arriba del umbral) y componentes
diminutos.
"""

import math

from app.core.component_analysis import analyze_svg_components
from app.core.errors import TooManySubpathsForComponentsError

DEFAULT_MAX_SUBPATHS = 20_000
DEFAULT_TOUCH_RATIO = 0.001
DEFAULT_TINY_AREA_RATIO = 0.0005


def _analyze(svg: str, touch_ratio: float = DEFAULT_TOUCH_RATIO, tiny_area_ratio: float = DEFAULT_TINY_AREA_RATIO) -> dict:
    return analyze_svg_components(svg, touch_ratio, tiny_area_ratio, DEFAULT_MAX_SUBPATHS)


# ---------------------------------------------------------------------------
# Islas separadas: 2+ formas sin ningún punto en común -> un componente cada una.
# ---------------------------------------------------------------------------

TWO_ISLANDS_SVG = (
    '<svg xmlns="http://www.w3.org/2000/svg" width="200" height="100">'
    '<path d="M0,0 L40,0 L40,40 L0,40 Z"/>'
    '<path d="M150,50 L190,50 L190,90 L150,90 Z"/>'
    "</svg>"
)


def test_two_separate_islands_are_two_components():
    result = _analyze(TWO_ISLANDS_SVG)

    assert len(result["components"]) == 2
    assert result["skipped_path_count"] == 0
    ids = [c["id"] for c in result["components"]]
    assert ids == ["component-1", "component-2"]
    for component in result["components"]:
        assert len(component["members"]) == 1
        assert component["members"][0]["role"] == "solid"


def test_islands_report_their_own_bounds_and_area():
    result = _analyze(TWO_ISLANDS_SVG)

    first, second = result["components"]
    assert first["bounds"] == {"min_x": 0.0, "min_y": 0.0, "max_x": 40.0, "max_y": 40.0}
    assert first["area"] == 1600.0
    assert second["bounds"] == {"min_x": 150.0, "min_y": 50.0, "max_x": 190.0, "max_y": 90.0}
    assert second["area"] == 1600.0


def test_three_islands_report_three_components_in_document_order():
    svg = (
        '<svg xmlns="http://www.w3.org/2000/svg" width="300" height="100">'
        '<path d="M0,0 L20,0 L20,20 L0,20 Z"/>'
        '<path d="M100,0 L120,0 L120,20 L100,20 Z"/>'
        '<path d="M200,0 L220,0 L220,20 L200,20 Z"/>'
        "</svg>"
    )

    result = _analyze(svg)

    assert [c["id"] for c in result["components"]] == ["component-1", "component-2", "component-3"]
    assert [len(c["members"]) for c in result["components"]] == [1, 1, 1]


# ---------------------------------------------------------------------------
# Agujeros internos: un subpath interior es un hueco, NO un componente aparte.
# ---------------------------------------------------------------------------

# Arandela: contorno exterior 0..100 con un agujero interior 30..70 -- mismo
# <path> con dos subpaths (topología "hierarchical=stacked" de VTracer,
# ver app.core.vector_engine), sentido de recorrido opuesto para el agujero
# (irrelevante para la detección de contención acá: se basa en
# point-in-polygon, no en el signo del winding).
WASHER_SVG = (
    '<svg xmlns="http://www.w3.org/2000/svg" width="100" height="100">'
    '<path d="M0,0 L100,0 L100,100 L0,100 Z M30,30 L30,70 L70,70 L70,30 Z"/>'
    "</svg>"
)


def test_ring_with_hole_is_a_single_component_with_a_hole_member():
    result = _analyze(WASHER_SVG)

    assert len(result["components"]) == 1
    component = result["components"][0]
    assert len(component["members"]) == 2
    roles = sorted(member["role"] for member in component["members"])
    assert roles == ["hole", "solid"]


def test_ring_with_hole_area_is_net_of_solid_minus_hole():
    result = _analyze(WASHER_SVG)

    component = result["components"][0]
    # Exterior 100x100=10000, agujero 40x40=1600 -> neto 8400.
    assert component["area"] == 8400.0


def test_solid_disk_resting_inside_a_ring_hole_without_touching_is_a_separate_component():
    # Un disco sólido (profundidad 2, PAR) suelto dentro del agujero de un
    # anillo (sin tocar su borde) es una pieza física SEPARADA -- la
    # contención sola no basta para fusionar cuando la profundidad es par.
    svg = (
        '<svg xmlns="http://www.w3.org/2000/svg" width="100" height="100">'
        '<path d="M0,0 L100,0 L100,100 L0,100 Z M20,20 L20,80 L80,80 L80,20 Z"/>'
        '<path d="M40,40 L40,60 L60,60 L60,40 Z"/>'
        "</svg>"
    )

    result = _analyze(svg)

    assert len(result["components"]) == 2
    member_counts = sorted(len(c["members"]) for c in result["components"])
    assert member_counts == [1, 2]


def test_triple_nested_squares_alternate_solid_hole_solid_by_depth_parity():
    # Tres cuadrados CONCÉNTRICOS anidados en cadena (0..100 exterior,
    # 20..80 en el medio, 40..60 interior), mismo <path> (topología
    # "hierarchical=stacked" de VTracer): profundidad 0 (exterior, PAR ->
    # sólido), profundidad 1 (medio, IMPAR -> agujero, se une a su padre
    # directo -- el exterior), profundidad 2 (interior, PAR -> sólido de
    # nuevo, por la regla even-odd -- ej. un disco visible en el centro de
    # una diana). El interior NO se une por contención (profundidad par) ni
    # por contacto (no toca el borde del agujero mediano, hay 20 unidades de
    # margen) -- queda como pieza física aparte, aunque esté geométricamente
    # anidado adentro del agujero del exterior.
    svg = (
        '<svg xmlns="http://www.w3.org/2000/svg" width="100" height="100">'
        '<path d="M0,0 L100,0 L100,100 L0,100 Z '
        "M20,20 L20,80 L80,80 L80,20 Z "
        'M40,40 L40,60 L60,60 L60,40 Z"/>'
        "</svg>"
    )

    result = _analyze(svg)

    assert len(result["components"]) == 2
    roles_by_component = sorted(
        tuple(sorted(member["role"] for member in c["members"])) for c in result["components"]
    )
    assert roles_by_component == [("hole", "solid"), ("solid",)]


# ---------------------------------------------------------------------------
# Piezas tocándose: comparten un punto/borde -> MISMO componente físico.
# ---------------------------------------------------------------------------


def test_two_shapes_sharing_an_edge_are_one_component():
    svg = (
        '<svg xmlns="http://www.w3.org/2000/svg" width="80" height="40">'
        '<path d="M0,0 L40,0 L40,40 L0,40 Z"/>'
        '<path d="M40,0 L80,0 L80,40 L40,40 Z"/>'
        "</svg>"
    )

    result = _analyze(svg)

    assert len(result["components"]) == 1
    assert len(result["components"][0]["members"]) == 2


def test_two_shapes_sharing_a_single_vertex_are_one_component():
    svg = (
        '<svg xmlns="http://www.w3.org/2000/svg" width="80" height="80">'
        '<path d="M0,0 L40,0 L40,40 L0,40 Z"/>'
        '<path d="M40,40 L80,40 L80,80 L40,80 Z"/>'
        "</svg>"
    )

    result = _analyze(svg)

    assert len(result["components"]) == 1


def test_two_shapes_far_apart_are_two_components():
    svg = (
        '<svg xmlns="http://www.w3.org/2000/svg" width="200" height="40">'
        '<path d="M0,0 L40,0 L40,40 L0,40 Z"/>'
        '<path d="M160,0 L200,0 L200,40 L160,40 Z"/>'
        "</svg>"
    )

    result = _analyze(svg)

    assert len(result["components"]) == 2


# ---------------------------------------------------------------------------
# Tolerancias: casos límite justo debajo/arriba del umbral de "tocarse".
# ---------------------------------------------------------------------------

_TOUCH_RATIO = 0.001
# Ancla usada SOLO para fijar un bounding box (y por lo tanto una diagonal)
# grande, sin que sus propios bounds contengan geométricamente a las formas
# chicas bajo prueba -- a diferencia del ancla de test_path_checker.py (que
# puede superponerse sin problema porque esa detección no interpreta
# contención), acá un ancla que ENCERRARA a las formas chicas las
# convertiría en "agujeros" suyos por el criterio de contención (ver
# app.core.component_analysis), rompiendo el escenario que este test quiere
# aislar. Se ubica en la esquina opuesta, sin overlap de bounding box.
_ANCHOR_SVG_FRAGMENT = '<path d="M990,990 L1000,990 L1000,1000 L990,1000 Z"/>'
_DIAGONAL = math.hypot(1000.0, 1000.0)
_TOLERANCE = _TOUCH_RATIO * _DIAGONAL


def _svg_with_gap(gap: float) -> str:
    return (
        f'<svg xmlns="http://www.w3.org/2000/svg" width="1000" height="1000">'
        f"{_ANCHOR_SVG_FRAGMENT}"
        f'<path d="M0,0 L40,0 L40,40 L0,40 Z"/>'
        f'<path d="M{40 + gap},0 L{80 + gap},0 L{80 + gap},40 L{40 + gap},40 Z"/>'
        "</svg>"
    )


def test_gap_just_below_touch_tolerance_merges_into_one_component():
    svg = _svg_with_gap(_TOLERANCE - 0.01)

    result = _analyze(svg, touch_ratio=_TOUCH_RATIO)

    # El ancla (cuadrado grande) es su propia isla: 1 (ancla) + 1 (las dos
    # chicas fusionadas) = 2 componentes.
    assert len(result["components"]) == 2
    small_component = next(c for c in result["components"] if len(c["members"]) == 2)
    assert len(small_component["members"]) == 2


def test_gap_just_above_touch_tolerance_keeps_two_components():
    svg = _svg_with_gap(_TOLERANCE + 0.01)

    result = _analyze(svg, touch_ratio=_TOUCH_RATIO)

    # Ancla + las dos chicas SIN fusionar = 3 componentes.
    assert len(result["components"]) == 3


# ---------------------------------------------------------------------------
# Componentes diminutos: se REPORTAN igual (nunca se filtran), marcados is_tiny.
# ---------------------------------------------------------------------------


def test_tiny_component_is_reported_and_flagged_not_dropped():
    # Cuadrado grande y cuadrado diminuto bien SEPARADOS (sin overlap de
    # bounding box): si el grande encerrara al chico, el chico pasaría a ser
    # un "agujero" suyo (ver app.core.component_analysis) en vez de un
    # componente propio, que no es lo que este test quiere aislar.
    svg = (
        '<svg xmlns="http://www.w3.org/2000/svg" width="301" height="301">'
        '<path d="M0,0 L200,0 L200,200 L0,200 Z"/>'
        '<path d="M300,300 L300.5,300 L300.5,300.5 L300,300.5 Z"/>'
        "</svg>"
    )

    result = _analyze(svg, tiny_area_ratio=DEFAULT_TINY_AREA_RATIO)

    assert len(result["components"]) == 2
    tiny = next(c for c in result["components"] if c["area"] < 1.0)
    assert tiny["is_tiny"] is True
    large = next(c for c in result["components"] if c["area"] > 1.0)
    assert large["is_tiny"] is False


def test_component_above_tiny_threshold_is_not_flagged():
    svg = TWO_ISLANDS_SVG

    result = _analyze(svg, tiny_area_ratio=DEFAULT_TINY_AREA_RATIO)

    assert all(component["is_tiny"] is False for component in result["components"])


# ---------------------------------------------------------------------------
# IDs estables / determinismo.
# ---------------------------------------------------------------------------


def test_analysis_is_deterministic():
    first = _analyze(TWO_ISLANDS_SVG)
    second = _analyze(TWO_ISLANDS_SVG)

    assert first == second


def test_same_svg_and_params_produce_the_same_ids_in_the_same_order():
    first = _analyze(TWO_ISLANDS_SVG)
    second = _analyze(TWO_ISLANDS_SVG)

    assert [c["id"] for c in first["components"]] == [c["id"] for c in second["components"]]


# ---------------------------------------------------------------------------
# Comandos no soportados: excluidos del análisis, contados como skipped.
# ---------------------------------------------------------------------------


def test_unsupported_command_paths_are_excluded_and_counted_as_skipped():
    svg = (
        '<svg xmlns="http://www.w3.org/2000/svg" width="50" height="50">'
        '<path d="M10,10 C20,0 30,0 40,10 L40,40 L10,40 Z"/>'
        '<path d="M0,0 L20,0 L20,20 L0,20 Z"/>'
        "</svg>"
    )

    result = _analyze(svg)

    assert result["skipped_path_count"] == 1
    assert len(result["components"]) == 1


def test_empty_svg_reports_no_components():
    empty_svg = '<svg xmlns="http://www.w3.org/2000/svg" width="10" height="10"></svg>'

    result = _analyze(empty_svg)

    assert result == {"components": [], "skipped_path_count": 0}


# ---------------------------------------------------------------------------
# Salvaguarda de rendimiento: demasiados subpaths analizables.
# ---------------------------------------------------------------------------


def test_too_many_subpaths_raises_typed_error():
    paths = "".join(f'<path d="M{i},{i} L{i + 1},{i} L{i + 1},{i + 1} Z"/>' for i in range(5))
    svg = f'<svg xmlns="http://www.w3.org/2000/svg" width="10" height="10">{paths}</svg>'

    try:
        analyze_svg_components(svg, DEFAULT_TOUCH_RATIO, DEFAULT_TINY_AREA_RATIO, max_subpaths=4)
        assert False, "se esperaba TooManySubpathsForComponentsError"
    except TooManySubpathsForComponentsError:
        pass
