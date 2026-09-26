"""Tests de app.core.physical_union (M2-S06): fusión de componentes físicos
YA calculados por M2-S03 en una única pieza fabricable, vía unión booleana
(Shapely) y/o bridging simple. Cubre los casos de prueba obligatorios de
spec.md M2-S06: piezas separadas (requieren bridge), selección de 3+ piezas
(grafo de conexión/MST), piezas con agujeros (preservados, no "rellenados"),
geometría inválida (autointersectante/degenerada -- debe fallar con mensaje
claro) y la validación post-operación no negociable ("nunca fingir unión").

Nota importante (ver IMPL.md del sprint, "Selección parcial y la rama
'solapadas/tangentes'"): dado que app.core.component_analysis (M2-S03) ya
funde CUALQUIER par de subpaths a distancia <= touch_tolerance en el MISMO
componente, dos componentes que el usuario puede seleccionar como
DISTINTOS están, por construcción, siempre a más de touch_tolerance de
distancia real entre sí -- así que la rama "ya se tocan/se superponen, no
hace falta bridge" de union_selected_components es, en un flujo end-to-end
real (selección de componentes YA calculados por M2-S03), matemáticamente
inalcanzable con la MISMA tolerancia reutilizada de M2-S03 (spec.md pide
explícitamente reutilizarla). Esa rama SÍ se seguía implementando (es
correcta, barata, y una salvaguarda razonable) y se testea acá de forma
DIRECTA sobre los helpers privados (`_minimum_spanning_tree`/
`_build_bridge`), sin pasar por `union_selected_components` completo (que
exigiría un `sanitized_svg` donde 2 "componentes" seleccionados estuvieran
realmente a distancia > 0 unos de otros y aun así <= touch_tolerance -- una
combinación imposible, ver arriba).
"""

import pytest
from shapely.geometry import Polygon

from app.core.component_analysis import analyze_svg_components
from app.core.errors import InvalidParametersError, PhysicalUnionImpossibleError, PhysicalUnionInvalidGeometryError
from app.core.physical_union import _build_bridge, _minimum_spanning_tree, union_selected_components
from app.core.svg_processing import sanitize_svg

DEFAULT_MAX_SUBPATHS = 20_000
DEFAULT_TOUCH_RATIO = 0.001
DEFAULT_TINY_AREA_RATIO = 0.0005
DEFAULT_BRIDGE_WIDTH_RATIO = 0.02


def _selection(component_id: str, members: list[dict]) -> dict:
    return {"component_id": component_id, "members": members}


def _solid(path_index: int, subpath_index: int = 0) -> dict:
    return {"path_index": path_index, "subpath_index": subpath_index, "role": "solid"}


def _hole(path_index: int, subpath_index: int) -> dict:
    return {"path_index": path_index, "subpath_index": subpath_index, "role": "hole"}


def _union(svg: str, selections: list[dict], touch_ratio: float = DEFAULT_TOUCH_RATIO) -> dict:
    return union_selected_components(
        sanitize_svg(svg), selections, touch_ratio, DEFAULT_TINY_AREA_RATIO, DEFAULT_BRIDGE_WIDTH_RATIO, DEFAULT_MAX_SUBPATHS
    )


# ---------------------------------------------------------------------------
# Piezas separadas: requieren bridge -- caso obligatorio de spec.md.
# ---------------------------------------------------------------------------

TWO_SEPARATED_SQUARES_SVG = (
    '<svg xmlns="http://www.w3.org/2000/svg" width="100" height="100">'
    '<path d="M0,0 L20,0 L20,20 L0,20 Z" fill="#000000"/>'
    '<path d="M70,70 L90,70 L90,90 L70,90 Z" fill="#000000"/>'
    "</svg>"
)

TWO_SEPARATED_SELECTIONS = [
    _selection("component-1", [_solid(0)]),
    _selection("component-2", [_solid(1)]),
]


def test_separated_pieces_get_bridged_into_a_single_component():
    result = _union(TWO_SEPARATED_SQUARES_SVG, TWO_SEPARATED_SELECTIONS)

    assert result["strategy"] == "bridge"
    assert result["bridge_count"] == 1
    assert result["component_count_before"] == 2
    assert result["component_count_after"] == 1
    assert result["expected_component_count_after"] == 1


def test_separated_pieces_result_reanalyzes_as_exactly_one_component():
    result = _union(TWO_SEPARATED_SQUARES_SVG, TWO_SEPARATED_SELECTIONS)

    reanalyzed = analyze_svg_components(result["svg"], DEFAULT_TOUCH_RATIO, DEFAULT_TINY_AREA_RATIO, DEFAULT_MAX_SUBPATHS)
    assert len(reanalyzed["components"]) == 1


def test_bridged_result_still_contains_the_original_material_area():
    result = _union(TWO_SEPARATED_SQUARES_SVG, TWO_SEPARATED_SELECTIONS)

    reanalyzed = analyze_svg_components(result["svg"], DEFAULT_TOUCH_RATIO, DEFAULT_TINY_AREA_RATIO, DEFAULT_MAX_SUBPATHS)
    merged_area = reanalyzed["components"][0]["area"]
    # 20x20 + 20x20 = 800 de material original; el bridge solo puede sumar
    # área (nunca restarla), así que el resultado nunca es MENOR al original.
    assert merged_area >= 800.0


def test_selection_with_fewer_than_two_components_is_rejected():
    with pytest.raises(InvalidParametersError):
        _union(TWO_SEPARATED_SQUARES_SVG, [TWO_SEPARATED_SELECTIONS[0]])


def test_duplicate_component_ids_in_selection_are_rejected():
    duplicated = [
        _selection("component-1", [_solid(0)]),
        _selection("component-1", [_solid(1)]),
    ]
    with pytest.raises(InvalidParametersError):
        _union(TWO_SEPARATED_SQUARES_SVG, duplicated)


def test_same_subpath_referenced_by_two_selections_is_rejected():
    overlapping_refs = [
        _selection("component-1", [_solid(0)]),
        _selection("component-2", [_solid(0)]),
    ]
    with pytest.raises(InvalidParametersError):
        _union(TWO_SEPARATED_SQUARES_SVG, overlapping_refs)


def test_unknown_member_reference_is_rejected():
    unknown_ref = [
        _selection("component-1", [_solid(0)]),
        _selection("component-2", [_solid(99)]),
    ]
    with pytest.raises(InvalidParametersError):
        _union(TWO_SEPARATED_SQUARES_SVG, unknown_ref)


# ---------------------------------------------------------------------------
# 3+ piezas: selección parcial -- se conectan todas vía un árbol de expansión
# mínima (MST), usando la pieza más cercana como referencia para cada bridge
# necesario (ver "Ambigüedades detectadas" de spec.md).
# ---------------------------------------------------------------------------

THREE_SEPARATED_SQUARES_SVG = (
    '<svg xmlns="http://www.w3.org/2000/svg" width="200" height="200">'
    '<path d="M0,0 L20,0 L20,20 L0,20 Z" fill="#000000"/>'
    '<path d="M40,0 L60,0 L60,20 L40,20 Z" fill="#000000"/>'
    '<path d="M150,150 L170,150 L170,170 L150,170 Z" fill="#000000"/>'
    "</svg>"
)

THREE_SEPARATED_SELECTIONS = [
    _selection("component-1", [_solid(0)]),
    _selection("component-2", [_solid(1)]),
    _selection("component-3", [_solid(2)]),
]


def test_three_separated_pieces_all_connect_into_one_component():
    result = _union(THREE_SEPARATED_SQUARES_SVG, THREE_SEPARATED_SELECTIONS)

    assert result["component_count_before"] == 3
    assert result["component_count_after"] == 1
    # Árbol de expansión mínima de 3 nodos -> exactamente 2 aristas -> 2 bridges.
    assert result["bridge_count"] == 2

    reanalyzed = analyze_svg_components(result["svg"], DEFAULT_TOUCH_RATIO, DEFAULT_TINY_AREA_RATIO, DEFAULT_MAX_SUBPATHS)
    assert len(reanalyzed["components"]) == 1


def test_partial_selection_leaves_non_selected_components_untouched():
    # 4ta pieza NO seleccionada: debe seguir existiendo, intacta, como su
    # propio componente separado tras la unión de las otras 3.
    svg = THREE_SEPARATED_SQUARES_SVG.replace(
        "</svg>", '<path d="M150,0 L170,0 L170,20 L150,20 Z" fill="#000000"/></svg>'
    )
    result = _union(svg, THREE_SEPARATED_SELECTIONS)

    assert result["component_count_before"] == 4
    assert result["component_count_after"] == 2  # 1 fusionada + 1 no tocada
    assert result["expected_component_count_after"] == 2


# ---------------------------------------------------------------------------
# Agujeros: deben preservarse en el resultado, nunca "rellenarse" por error.
# ---------------------------------------------------------------------------

WASHER_SEPARATED_FROM_DISK_SVG = (
    '<svg xmlns="http://www.w3.org/2000/svg" width="100" height="100">'
    '<path d="M0,0 L60,0 L60,60 L0,60 Z M20,20 L20,40 L40,40 L40,20 Z" fill="#000000"/>'
    '<path d="M70,20 L90,20 L90,40 L70,40 Z" fill="#000000"/>'
    "</svg>"
)

WASHER_SELECTIONS = [
    _selection("component-1", [_solid(0), _hole(0, 1)]),
    _selection("component-2", [_solid(1)]),
]


def test_hole_is_preserved_after_bridging_a_washer_to_a_separate_disk():
    result = _union(WASHER_SEPARATED_FROM_DISK_SVG, WASHER_SELECTIONS)

    reanalyzed = analyze_svg_components(result["svg"], DEFAULT_TOUCH_RATIO, DEFAULT_TINY_AREA_RATIO, DEFAULT_MAX_SUBPATHS)
    assert len(reanalyzed["components"]) == 1
    roles = sorted(member["role"] for member in reanalyzed["components"][0]["members"])
    assert "hole" in roles


def test_hole_area_is_still_subtracted_not_filled_in():
    result = _union(WASHER_SEPARATED_FROM_DISK_SVG, WASHER_SELECTIONS)

    reanalyzed = analyze_svg_components(result["svg"], DEFAULT_TOUCH_RATIO, DEFAULT_TINY_AREA_RATIO, DEFAULT_MAX_SUBPATHS)
    merged_area = reanalyzed["components"][0]["area"]
    # Exterior 60x60=3600, agujero 20x20=400, disco 20x20=400 -> neto de
    # material (sin contar el bridge) = 3600-400+400 = 3600. Si el agujero
    # se hubiera "rellenado" por error, el área sería >= 4000 (3600+400).
    # Se deja margen para el área que aporta el bridge.
    assert merged_area < 4000.0


# ---------------------------------------------------------------------------
# Geometría inválida: debe fallar con un mensaje claro, nunca un resultado
# corrupto silencioso.
# ---------------------------------------------------------------------------

SELF_INTERSECTING_BOWTIE_SVG = (
    '<svg xmlns="http://www.w3.org/2000/svg" width="100" height="100">'
    '<path d="M0,0 L40,40 L40,0 L0,40 Z" fill="#000000"/>'
    '<path d="M60,60 L100,60 L100,100 L60,100 Z" fill="#000000"/>'
    "</svg>"
)


def test_self_intersecting_component_is_rejected_with_a_clear_message():
    with pytest.raises(PhysicalUnionInvalidGeometryError, match="component-1"):
        _union(SELF_INTERSECTING_BOWTIE_SVG, TWO_SEPARATED_SELECTIONS)


def test_self_intersecting_component_never_produces_a_result():
    """Nunca debe llegar a construirse un `svg` de resultado -- la excepción
    se lanza ANTES de tocar el documento, no después de generar algo
    corrupto."""
    try:
        _union(SELF_INTERSECTING_BOWTIE_SVG, TWO_SEPARATED_SELECTIONS)
        pytest.fail("Se esperaba PhysicalUnionInvalidGeometryError")
    except PhysicalUnionInvalidGeometryError as exc:
        assert "d=" not in str(exc)  # el mensaje no filtra geometría cruda, es explicativo


DEGENERATE_TWO_POINT_SVG = (
    '<svg xmlns="http://www.w3.org/2000/svg" width="100" height="100">'
    '<path d="M0,0 L20,20" fill="#000000"/>'
    '<path d="M70,70 L90,70 L90,90 L70,90 Z" fill="#000000"/>'
    "</svg>"
)


def test_degenerate_two_point_subpath_is_rejected():
    with pytest.raises(PhysicalUnionInvalidGeometryError):
        _union(DEGENERATE_TWO_POINT_SVG, TWO_SEPARATED_SELECTIONS)


# ---------------------------------------------------------------------------
# Validación post-operación no negociable ("nunca fingir unión"): si el
# conteo de componentes tras la unión no coincide EXACTAMENTE con el
# esperado, la operación debe fallar en vez de devolver un resultado.
# ---------------------------------------------------------------------------


def test_never_fake_a_union_when_the_result_does_not_reanalyze_as_expected(monkeypatch):
    """Simula que la unión booleana/bridging "parece" haber conectado las
    piezas pero el análisis de componentes posterior (el MISMO analizador de
    M2-S03, invocado una SEGUNDA vez sobre el SVG resultante como validación
    post-operación no negociable) sigue viendo más de 1 pieza -- la
    operación debe fallar explícitamente, NUNCA devolver ese resultado como
    si hubiera funcionado. Se intercepta la segunda llamada (la de
    validación) para que reporte un conteo inflado, sin tocar la primera
    (el conteo real "antes" sigue siendo el legítimo)."""
    import app.core.physical_union as physical_union_module

    call_count = {"n": 0}
    original_analyze = physical_union_module.analyze_svg_components

    def _fake_analyze(svg_text, touch_ratio, tiny_area_ratio, max_subpaths):
        call_count["n"] += 1
        result = original_analyze(svg_text, touch_ratio, tiny_area_ratio, max_subpaths)
        if call_count["n"] == 2:
            # Segunda llamada = la validación POST-operación (ver docstring
            # del módulo, punto 6): se simula que, pese a que la geometría
            # "salió" del pipeline booleano/bridging, el analizador real
            # seguiría viendo más de 1 componente -- el caso exacto que la
            # validación no negociable debe atrapar.
            duplicated = result["components"] + result["components"]
            return {"components": duplicated, "skipped_path_count": result["skipped_path_count"]}
        return result

    monkeypatch.setattr(physical_union_module, "analyze_svg_components", _fake_analyze)

    with pytest.raises(PhysicalUnionImpossibleError):
        _union(TWO_SEPARATED_SQUARES_SVG, TWO_SEPARATED_SELECTIONS)

    assert call_count["n"] == 2  # confirma que efectivamente se corrió la validación post-operación


# ---------------------------------------------------------------------------
# Helpers privados: árbol de expansión mínima y construcción de bridges --
# ver nota del docstring del módulo sobre por qué la rama "ya se tocan, no
# hace falta bridge" no es alcanzable end-to-end en este sistema.
# ---------------------------------------------------------------------------


def test_minimum_spanning_tree_connects_all_nodes_with_n_minus_one_edges():
    geoms = [
        Polygon([(0, 0), (10, 0), (10, 10), (0, 10)]),
        Polygon([(100, 0), (110, 0), (110, 10), (100, 10)]),
        Polygon([(50, 100), (60, 100), (60, 110), (50, 110)]),
    ]
    edges = _minimum_spanning_tree(geoms)
    assert len(edges) == 2
    connected = {0}
    for source, target, _distance in edges:
        assert source in connected
        connected.add(target)
    assert connected == {0, 1, 2}


def test_build_bridge_returns_none_when_pieces_already_touch():
    square_a = Polygon([(0, 0), (10, 0), (10, 10), (0, 10)])
    square_b = Polygon([(10, 0), (20, 0), (20, 10), (10, 10)])  # comparte el borde x=10
    assert _build_bridge(square_a, square_b, bridge_width=2.0) is None


def test_build_bridge_overlaps_both_separated_pieces():
    square_a = Polygon([(0, 0), (10, 0), (10, 10), (0, 10)])
    square_b = Polygon([(50, 0), (60, 0), (60, 10), (50, 10)])
    bridge = _build_bridge(square_a, square_b, bridge_width=2.0)

    assert bridge is not None
    # El bridge se extiende MÁS ALLÁ de cada punto más cercano hacia adentro
    # de cada pieza: debe solaparse (distancia real 0) con ambas, no solo
    # tocarlas en un punto teórico.
    assert bridge.distance(square_a) == 0.0
    assert bridge.distance(square_b) == 0.0
