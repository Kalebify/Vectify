"""Tests del servicio de orquestación (PhysicalUnionService): reproducibilidad,
respeto del límite de tamaño de entrada, rechazo controlado de SVG de entrada
inválido/corrupto, timeout de la unión (inyectado como fake) y que la
respuesta tipada refleje correctamente el resultado del núcleo geométrico.
Mismo criterio que test_component_analysis_service.py.
"""

import time

import pytest

from app.core.config import Settings
from app.core.errors import InvalidInputSvgError, PhysicalUnionTimeoutError, SvgInputTooLargeError
from app.models.schemas import PhysicalUnionMemberRef, PhysicalUnionParams, PhysicalUnionSelection
from app.services.physical_union_service import PhysicalUnionService

TWO_SEPARATED_SQUARES_SVG = (
    '<svg xmlns="http://www.w3.org/2000/svg" width="100" height="100">'
    '<path d="M0,0 L20,0 L20,20 L0,20 Z" fill="#000000"/>'
    '<path d="M70,70 L90,70 L90,90 L70,90 Z" fill="#000000"/>'
    "</svg>"
).encode("utf-8")


def _params() -> PhysicalUnionParams:
    return PhysicalUnionParams(
        selections=[
            PhysicalUnionSelection(component_id="component-1", members=[PhysicalUnionMemberRef(path_index=0, subpath_index=0, role="solid")]),
            PhysicalUnionSelection(component_id="component-2", members=[PhysicalUnionMemberRef(path_index=1, subpath_index=0, role="solid")]),
        ]
    )


def _slow_union(svg_text, selections, touch_ratio, tiny_area_ratio, bridge_width_ratio, max_subpaths) -> dict:
    time.sleep(0.5)
    return {
        "svg": svg_text,
        "component_count_before": 2,
        "component_count_after": 1,
        "expected_component_count_after": 1,
        "strategy": "bridge",
        "bridge_count": 1,
    }


@pytest.fixture()
def service() -> PhysicalUnionService:
    return PhysicalUnionService(Settings())


def test_process_returns_reproducible_result_for_same_input(service):
    params = _params()

    first = service.process(TWO_SEPARATED_SQUARES_SVG, params)
    second = service.process(TWO_SEPARATED_SQUARES_SVG, params)

    assert first == second


def test_process_merges_two_separated_squares_into_one_component(service):
    result = service.process(TWO_SEPARATED_SQUARES_SVG, _params())

    assert result.component_count_before == 2
    assert result.component_count_after == 1
    assert result.strategy == "bridge"
    assert result.bridge_count == 1
    assert "<path" in result.svg


def test_process_reports_canvas_dimensions_from_the_root_svg(service):
    result = service.process(TWO_SEPARATED_SQUARES_SVG, _params())

    assert result.width == 100
    assert result.height == 100


def test_process_does_not_mutate_input_bytes(service):
    original_copy = bytes(TWO_SEPARATED_SQUARES_SVG)

    service.process(TWO_SEPARATED_SQUARES_SVG, _params())

    assert TWO_SEPARATED_SQUARES_SVG == original_copy


def test_process_raises_invalid_input_svg_error_for_non_utf8_bytes(service):
    with pytest.raises(InvalidInputSvgError):
        service.process(b"\xff\xfe\x00\x01", _params())


def test_process_raises_invalid_input_svg_error_for_malformed_xml(service):
    with pytest.raises(InvalidInputSvgError):
        service.process(b"<svg><path d='M0,0'></svg-not-closed>", _params())


def test_process_raises_svg_input_too_large_when_input_exceeds_configured_limit():
    tiny_input_service = PhysicalUnionService(Settings(max_svg_output_bytes=10))

    with pytest.raises(SvgInputTooLargeError):
        tiny_input_service.process(TWO_SEPARATED_SQUARES_SVG, _params())


def test_process_raises_timeout_when_union_takes_too_long():
    slow_service = PhysicalUnionService(Settings(physical_union_timeout_seconds=0), union_fn=_slow_union)

    with pytest.raises(PhysicalUnionTimeoutError):
        slow_service.process(TWO_SEPARATED_SQUARES_SVG, _params())


def test_process_returns_promptly_on_timeout_even_if_union_keeps_running():
    """Regresión: `_union_with_timeout` no debe bloquear esperando a que el
    hilo colgado termine -- mismo criterio que el test equivalente de
    ComponentAnalysisService."""

    def very_slow_union(svg_text, selections, touch_ratio, tiny_area_ratio, bridge_width_ratio, max_subpaths) -> dict:
        time.sleep(3.0)
        return {
            "svg": svg_text,
            "component_count_before": 2,
            "component_count_after": 1,
            "expected_component_count_after": 1,
            "strategy": "bridge",
            "bridge_count": 1,
        }

    slow_service = PhysicalUnionService(Settings(physical_union_timeout_seconds=1), union_fn=very_slow_union)

    start = time.perf_counter()
    with pytest.raises(PhysicalUnionTimeoutError):
        slow_service.process(TWO_SEPARATED_SQUARES_SVG, _params())
    elapsed = time.perf_counter() - start

    assert elapsed < 2.0


def test_process_sanitizes_dangerous_content_before_uniting(service):
    malicious_svg = (
        '<svg xmlns="http://www.w3.org/2000/svg" width="100" height="100">'
        "<script>alert(1)</script>"
        '<path d="M0,0 L20,0 L20,20 L0,20 Z" fill="#000000" onload="alert(2)"/>'
        '<path d="M70,70 L90,70 L90,90 L70,90 Z" fill="#000000"/>'
        "</svg>"
    ).encode("utf-8")

    result = service.process(malicious_svg, _params())

    assert "<script" not in result.svg
    assert "onload" not in result.svg
