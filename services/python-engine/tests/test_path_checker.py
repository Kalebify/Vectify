"""Tests de app.core.path_checker: detección de paths abiertos y
duplicados/casi-duplicados sobre SVGs de ejemplo escritos a mano -- no
dependen de VTracer ni de PathCheckerService (eso lo cubre
test_path_checker_service.py). Cubre los casos de prueba obligatorios de
spec.md M1-S08: duplicado exacto, casi-duplicado (dentro y fuera de
tolerancia -- casos límite justo debajo/arriba del umbral), path abierto
chico/grande, diseño correcto (cero issues) y casos conocidos de falso
positivo verificados.
"""

import math

from app.core.errors import TooManySubpathsError
from app.core.path_checker import analyze_svg_paths

DEFAULT_MAX_SUBPATHS = 20_000


def _analyze(svg: str, close_gap_ratio: float = 0.005, duplicate_point_ratio: float = 0.002) -> dict:
    return analyze_svg_paths(svg, close_gap_ratio, duplicate_point_ratio, DEFAULT_MAX_SUBPATHS)


# ---------------------------------------------------------------------------
# Diseño correcto: sin issues, sin falsos positivos.
# ---------------------------------------------------------------------------

CORRECT_DESIGN_SVG = (
    '<svg xmlns="http://www.w3.org/2000/svg" width="200" height="100">'
    '<path d="M0,0 L100,0 L100,80 L0,80 Z"/>'
    '<path d="M150,10 L190,10 L190,40 L150,40 Z"/>'
    "</svg>"
)


def test_correct_design_reports_no_issues():
    result = _analyze(CORRECT_DESIGN_SVG)

    assert result["open_path_issues"] == []
    assert result["duplicate_issues"] == []
    assert result["skipped_path_count"] == 0


# ---------------------------------------------------------------------------
# Paths abiertos que deberían estar cerrados -- chico y grande.
# ---------------------------------------------------------------------------


def test_small_open_path_that_should_be_closed_is_detected():
    # Diseño chico (10x10): gap entre inicio y fin bien por debajo de la
    # tolerancia relativa por defecto (0.5% de la diagonal ~14.14 => ~0.0707).
    svg = (
        '<svg xmlns="http://www.w3.org/2000/svg" width="10" height="10">'
        '<path d="M0,0 L10,0 L5,10 L0.03,0.02"/>'
        "</svg>"
    )

    result = _analyze(svg)

    assert len(result["open_path_issues"]) == 1
    issue = result["open_path_issues"][0]
    assert issue["type"] == "open_path"
    assert issue["severity"] == "error"
    assert issue["path_index"] == 0
    assert issue["subpath_index"] == 0
    assert issue["gap_distance"] < 0.0707


def test_large_open_path_that_should_be_closed_is_detected():
    # Diseño grande (1000x1000): gap absoluto de 5 unidades, chico en
    # términos relativos a la diagonal (~1414.2), por debajo de la
    # tolerancia (~7.07).
    svg = (
        '<svg xmlns="http://www.w3.org/2000/svg" width="1000" height="1000">'
        '<path d="M0,0 L1000,0 L1000,1000 L500,900 L5,3"/>'
        "</svg>"
    )

    result = _analyze(svg)

    assert len(result["open_path_issues"]) == 1
    issue = result["open_path_issues"][0]
    assert issue["gap_distance"] == math.hypot(5, 3)


def test_open_path_with_gap_larger_than_tolerance_is_not_flagged():
    # Mismo diseño chico, pero con un gap deliberadamente grande (no debería
    # estar cerrado -- ej. una forma tipo "C" con una abertura real).
    svg = (
        '<svg xmlns="http://www.w3.org/2000/svg" width="10" height="10">'
        '<path d="M0,0 L10,0 L10,10 L0,10 L0,3"/>'
        "</svg>"
    )

    result = _analyze(svg)

    assert result["open_path_issues"] == []


def test_already_closed_subpath_is_never_flagged_as_open():
    svg = (
        '<svg xmlns="http://www.w3.org/2000/svg" width="10" height="10">'
        '<path d="M0,0 L10,0 L5,10 Z"/>'
        "</svg>"
    )

    result = _analyze(svg)

    assert result["open_path_issues"] == []


# ---------------------------------------------------------------------------
# Casos conocidos de falso positivo para "path abierto" -- verificados a
# propósito para que NO se disparen.
# ---------------------------------------------------------------------------


def test_known_false_positive_two_point_degenerate_line_is_not_flagged():
    # Un subpath de 2 puntos (una línea, sin `Z`) cuyos extremos coinciden
    # (o casi) NO tiene un área que "cerrar" -- es geométricamente un punto
    # degenerado, no una forma. Sin la salvaguarda de >=3 puntos, este caso
    # dispararía un falso positivo.
    svg = '<svg xmlns="http://www.w3.org/2000/svg" width="10" height="10"><path d="M5,5 L5.001,5.001"/></svg>'

    result = _analyze(svg)

    assert result["open_path_issues"] == []


def test_known_false_positive_unsupported_command_path_is_never_analyzed():
    # Un path con un comando no soportado (curva "C") cuyo `d` "parece"
    # casi cerrado (el último punto de control está cerca del primero) NO
    # debe analizarse -- no hay forma segura de interpretar sus coordenadas
    # reales sin soporte de curvas, así que se excluye del análisis por
    # completo en vez de arriesgar un falso positivo/negativo.
    svg = (
        '<svg xmlns="http://www.w3.org/2000/svg" width="50" height="50">'
        '<path d="M10,10 C20,0 30,0 40,10 L40,40 L10,40 L10.01,10.01"/>'
        "</svg>"
    )

    result = _analyze(svg)

    assert result["open_path_issues"] == []
    assert result["duplicate_issues"] == []
    assert result["skipped_path_count"] == 1


# ---------------------------------------------------------------------------
# Duplicado exacto.
# ---------------------------------------------------------------------------

EXACT_DUPLICATE_SVG = (
    '<svg xmlns="http://www.w3.org/2000/svg" width="20" height="20">'
    '<path d="M2,2 L8,2 L8,8 L2,8 Z"/>'
    '<path d="M2,2 L8,2 L8,8 L2,8 Z"/>'
    "</svg>"
)


def test_exact_duplicate_paths_are_detected_as_one_group():
    result = _analyze(EXACT_DUPLICATE_SVG)

    assert result["open_path_issues"] == []
    assert len(result["duplicate_issues"]) == 1
    issue = result["duplicate_issues"][0]
    assert issue["type"] == "duplicate_path"
    assert issue["exact"] is True
    assert issue["severity"] == "error"
    assert issue["max_point_distance"] == 0.0
    assert len(issue["members"]) == 2
    assert [m["path_index"] for m in issue["members"]] == [0, 1]


def test_exact_duplicate_is_detected_regardless_of_traversal_direction():
    # El segundo path traza el mismo cuadrado en sentido inverso (ej. un
    # agujero vs. un contorno) -- sigue siendo el mismo contorno geométrico.
    svg = (
        '<svg xmlns="http://www.w3.org/2000/svg" width="20" height="20">'
        '<path d="M2,2 L8,2 L8,8 L2,8 Z"/>'
        '<path d="M2,8 L8,8 L8,2 L2,2 Z"/>'
        "</svg>"
    )

    result = _analyze(svg)

    assert len(result["duplicate_issues"]) == 1
    assert result["duplicate_issues"][0]["exact"] is True


# ---------------------------------------------------------------------------
# Casi-duplicado: casos límite justo debajo/arriba del umbral.
# ---------------------------------------------------------------------------

_ANCHOR_SIZE = 1000.0
_SMALL_ORIGIN = (100.0, 100.0)
_SMALL_SIZE = 10.0
_DUPLICATE_POINT_RATIO = 0.002
# Diagonal determinada exclusivamente por el ancla (1000x1000): el
# cuadradito chico y su copia desplazada quedan muy adentro de esos bounds
# (100..112.83 aprox.), así que un shift de unas pocas unidades nunca cambia
# el bounding box general -- la tolerancia calculada acá coincide exactamente
# con la que usa analyze_svg_paths internamente.
_DIAGONAL = math.hypot(_ANCHOR_SIZE, _ANCHOR_SIZE)
_TOLERANCE = _DUPLICATE_POINT_RATIO * _DIAGONAL


def _svg_with_shifted_duplicate(shift: float) -> str:
    ox, oy = _SMALL_ORIGIN
    original = f"M{ox},{oy} L{ox + _SMALL_SIZE},{oy} L{ox + _SMALL_SIZE},{oy + _SMALL_SIZE} L{ox},{oy + _SMALL_SIZE} Z"
    shifted = (
        f"M{ox + shift},{oy} L{ox + _SMALL_SIZE + shift},{oy} "
        f"L{ox + _SMALL_SIZE + shift},{oy + _SMALL_SIZE} L{ox + shift},{oy + _SMALL_SIZE} Z"
    )
    return (
        f'<svg xmlns="http://www.w3.org/2000/svg" width="1000" height="1000">'
        f'<path d="M0,0 L{_ANCHOR_SIZE},0 L{_ANCHOR_SIZE},{_ANCHOR_SIZE} L0,{_ANCHOR_SIZE} Z"/>'
        f'<path d="{original}"/>'
        f'<path d="{shifted}"/>'
        "</svg>"
    )


def test_near_duplicate_just_below_tolerance_is_flagged():
    svg = _svg_with_shifted_duplicate(_TOLERANCE - 0.01)

    result = _analyze(svg, duplicate_point_ratio=_DUPLICATE_POINT_RATIO)

    assert len(result["duplicate_issues"]) == 1
    issue = result["duplicate_issues"][0]
    assert issue["exact"] is False
    assert issue["severity"] == "warning"
    assert issue["max_point_distance"] < _TOLERANCE


def test_near_duplicate_just_above_tolerance_is_not_flagged():
    svg = _svg_with_shifted_duplicate(_TOLERANCE + 0.01)

    result = _analyze(svg, duplicate_point_ratio=_DUPLICATE_POINT_RATIO)

    assert result["duplicate_issues"] == []


# ---------------------------------------------------------------------------
# Falso positivo conocido para duplicados: formas parecidas pero
# geométricamente distintas NO deben agruparse.
# ---------------------------------------------------------------------------


def test_known_false_positive_similar_but_different_sized_shapes_are_not_duplicates():
    svg = (
        '<svg xmlns="http://www.w3.org/2000/svg" width="200" height="200">'
        '<path d="M0,0 L10,0 L10,10 L0,10 Z"/>'
        '<path d="M0,0 L100,0 L100,100 L0,100 Z"/>'
        "</svg>"
    )

    result = _analyze(svg)

    assert result["duplicate_issues"] == []


def test_duplicate_group_with_three_members_reports_one_issue():
    svg = (
        '<svg xmlns="http://www.w3.org/2000/svg" width="20" height="20">'
        '<path d="M2,2 L8,2 L8,8 L2,8 Z"/>'
        '<path d="M2,2 L8,2 L8,8 L2,8 Z"/>'
        '<path d="M2,2 L8,2 L8,8 L2,8 Z"/>'
        "</svg>"
    )

    result = _analyze(svg)

    assert len(result["duplicate_issues"]) == 1
    assert len(result["duplicate_issues"][0]["members"]) == 3


# ---------------------------------------------------------------------------
# Comandos no soportados: excluidos del análisis por completo.
# ---------------------------------------------------------------------------


def test_unsupported_command_paths_are_excluded_and_counted_as_skipped():
    svg = (
        '<svg xmlns="http://www.w3.org/2000/svg" width="50" height="50">'
        '<path d="M10,10 C20,0 30,0 40,10 L40,40 L10,40 Z"/>'
        '<path d="M0,0 H40 V40 H0 Z"/>'
        '<path d="m0,0 l10,0 l0,10 z"/>'
        "</svg>"
    )

    result = _analyze(svg)

    assert result["open_path_issues"] == []
    assert result["duplicate_issues"] == []
    assert result["skipped_path_count"] == 3


# ---------------------------------------------------------------------------
# Determinismo y orden reproducible.
# ---------------------------------------------------------------------------


def test_analysis_is_deterministic():
    svg = (
        '<svg xmlns="http://www.w3.org/2000/svg" width="100" height="100">'
        '<path d="M0,0 L50,0 L25,50 L0.02,0.01"/>'
        '<path d="M60,60 L90,60 L90,90 L60,90 Z"/>'
        '<path d="M60,60 L90,60 L90,90 L60,90 Z"/>'
        "</svg>"
    )

    first = _analyze(svg)
    second = _analyze(svg)

    assert first == second


def test_open_path_issues_are_reported_in_document_order():
    svg = (
        '<svg xmlns="http://www.w3.org/2000/svg" width="200" height="200">'
        '<path d="M60,60 L90,60 L60,90 L60.02,60.01"/>'
        '<path d="M0,0 L50,0 L0,50 L0.02,0.01"/>'
        "</svg>"
    )

    result = _analyze(svg)

    assert [issue["path_index"] for issue in result["open_path_issues"]] == [0, 1]


def test_empty_svg_reports_no_issues():
    empty_svg = '<svg xmlns="http://www.w3.org/2000/svg" width="10" height="10"></svg>'

    result = _analyze(empty_svg)

    assert result == {"open_path_issues": [], "duplicate_issues": [], "skipped_path_count": 0}


# ---------------------------------------------------------------------------
# Salvaguarda de rendimiento: demasiados subpaths analizables.
# ---------------------------------------------------------------------------


def test_too_many_subpaths_raises_typed_error():
    paths = "".join(f'<path d="M{i},{i} L{i + 1},{i} L{i + 1},{i + 1} Z"/>' for i in range(5))
    svg = f'<svg xmlns="http://www.w3.org/2000/svg" width="10" height="10">{paths}</svg>'

    try:
        analyze_svg_paths(svg, 0.005, 0.002, max_subpaths=4)
        assert False, "se esperaba TooManySubpathsError"
    except TooManySubpathsError:
        pass
