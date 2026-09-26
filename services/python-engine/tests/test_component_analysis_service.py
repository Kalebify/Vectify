"""Tests del servicio de orquestación (ComponentAnalysisService):
reproducibilidad, respeto del límite de tamaño de entrada, rechazo
controlado de SVG de entrada inválido/corrupto y timeout del análisis
(inyectado como fake, sin depender de que un SVG real sea lo bastante grande
como para tardar). Mismo criterio que test_path_checker_service.py.
"""

import time

import pytest

from app.core.config import Settings
from app.core.errors import ComponentAnalysisTimeoutError, InvalidInputSvgError, SvgInputTooLargeError
from app.models.schemas import ComponentAnalysisParams
from app.services.component_analysis_service import ComponentAnalysisService

TWO_SQUARES_SVG = (
    '<svg xmlns="http://www.w3.org/2000/svg" width="80" height="40">'
    '<path d="M0,0 L40,0 L40,40 L0,40 Z"/>'
    '<path d="M40,0 L80,0 L80,40 L40,40 Z"/>'
    "</svg>"
).encode("utf-8")


def _slow_analyze(svg_text: str, touch_ratio: float, tiny_area_ratio: float, max_subpaths: int) -> dict:
    time.sleep(0.5)
    return {"components": [], "skipped_path_count": 0}


@pytest.fixture()
def service() -> ComponentAnalysisService:
    return ComponentAnalysisService(Settings())


def test_process_returns_reproducible_result_for_same_input(service):
    params = ComponentAnalysisParams()

    first = service.process(TWO_SQUARES_SVG, params)
    second = service.process(TWO_SQUARES_SVG, params)

    assert first == second


def test_process_detects_the_touching_squares_as_one_component_and_reports_summary(service):
    params = ComponentAnalysisParams()

    result = service.process(TWO_SQUARES_SVG, params)

    assert result.summary.component_count == 1
    assert len(result.components) == 1
    assert len(result.components[0].members) == 2


def test_process_does_not_mutate_input_bytes(service):
    original_copy = bytes(TWO_SQUARES_SVG)
    params = ComponentAnalysisParams()

    service.process(TWO_SQUARES_SVG, params)

    assert TWO_SQUARES_SVG == original_copy


def test_process_raises_invalid_input_svg_error_for_non_utf8_bytes(service):
    params = ComponentAnalysisParams()

    with pytest.raises(InvalidInputSvgError):
        service.process(b"\xff\xfe\x00\x01", params)


def test_process_raises_invalid_input_svg_error_for_malformed_xml(service):
    params = ComponentAnalysisParams()

    with pytest.raises(InvalidInputSvgError):
        service.process(b"<svg><path d='M0,0'></svg-not-closed>", params)


def test_process_raises_invalid_input_svg_error_for_non_svg_root(service):
    params = ComponentAnalysisParams()

    with pytest.raises(InvalidInputSvgError):
        service.process(b'<html xmlns="http://www.w3.org/2000/svg"></html>', params)


def test_process_raises_svg_input_too_large_when_input_exceeds_configured_limit():
    tiny_input_service = ComponentAnalysisService(Settings(max_svg_output_bytes=10))
    params = ComponentAnalysisParams()

    with pytest.raises(SvgInputTooLargeError):
        tiny_input_service.process(TWO_SQUARES_SVG, params)


def test_process_raises_timeout_when_analysis_takes_too_long():
    slow_service = ComponentAnalysisService(Settings(component_timeout_seconds=0), analyze_fn=_slow_analyze)
    params = ComponentAnalysisParams()

    with pytest.raises(ComponentAnalysisTimeoutError):
        slow_service.process(TWO_SQUARES_SVG, params)


def test_process_returns_promptly_on_timeout_even_if_analysis_keeps_running():
    """Regresión: `_analyze_with_timeout` no debe bloquear esperando a que el
    hilo colgado termine -- debe retornar (lanzando
    ComponentAnalysisTimeoutError) cerca del timeout configurado, no del
    delay real de la función lenta. Mismo criterio que el test equivalente
    de PathCheckerService."""

    def very_slow_analyze(svg_text: str, touch_ratio: float, tiny_area_ratio: float, max_subpaths: int) -> dict:
        time.sleep(3.0)
        return {"components": [], "skipped_path_count": 0}

    slow_service = ComponentAnalysisService(Settings(component_timeout_seconds=1), analyze_fn=very_slow_analyze)
    params = ComponentAnalysisParams()

    start = time.perf_counter()
    with pytest.raises(ComponentAnalysisTimeoutError):
        slow_service.process(TWO_SQUARES_SVG, params)
    elapsed = time.perf_counter() - start

    assert elapsed < 2.0


def test_process_sanitizes_dangerous_content_before_analyzing(service):
    malicious_svg = (
        '<svg xmlns="http://www.w3.org/2000/svg" width="60" height="60">'
        "<script>alert(1)</script>"
        '<path d="M10,10 L10,20 L10,30 L50,50 L50,10 Z" onload="alert(2)"/>'
        "</svg>"
    ).encode("utf-8")
    params = ComponentAnalysisParams()

    # No debe lanzar -- el <script>/onload se sanean antes de analizar, y el
    # análisis en sí nunca vuelve a exponer el SVG (es de solo lectura).
    result = service.process(malicious_svg, params)

    assert result.skipped_path_count == 0


def test_process_never_mentions_the_svg_content_in_the_response():
    # El análisis de componentes es de solo lectura: la respuesta nunca
    # debería incluir un campo con el SVG completo (a diferencia de
    # SimplifyResponse) -- solo índices/coordenadas de componentes.
    params = ComponentAnalysisParams()

    result = ComponentAnalysisService(Settings()).process(TWO_SQUARES_SVG, params)

    assert not hasattr(result, "svg")


def test_process_reports_tiny_component_flagged_not_dropped(service):
    svg = (
        '<svg xmlns="http://www.w3.org/2000/svg" width="201" height="201">'
        '<path d="M0,0 L100,0 L100,100 L0,100 Z"/>'
        '<path d="M200,200 L200.3,200 L200.3,200.3 L200,200.3 Z"/>'
        "</svg>"
    ).encode("utf-8")
    params = ComponentAnalysisParams()

    result = service.process(svg, params)

    assert result.summary.component_count == 2
    assert result.summary.tiny_component_count == 1
    tiny = next(c for c in result.components if c.is_tiny)
    assert len(tiny.members) == 1
