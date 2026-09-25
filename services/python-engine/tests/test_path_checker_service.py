"""Tests del servicio de orquestación (PathCheckerService): reproducibilidad,
respeto del límite de tamaño de entrada, rechazo controlado de SVG de entrada
inválido/corrupto y timeout del análisis (inyectado como fake, sin depender
de que un SVG real sea lo bastante grande como para tardar). Mismo criterio
que test_simplification_service.py.
"""

import time

import pytest

from app.core.config import Settings
from app.core.errors import CheckTimeoutError, InvalidInputSvgError, SvgInputTooLargeError
from app.models.schemas import CheckParams
from app.services.path_checker_service import PathCheckerService

OPEN_SQUARE_SVG = (
    '<svg xmlns="http://www.w3.org/2000/svg" width="60" height="60">'
    '<path d="M10,10 L50,10 L50,50 L10,50 L10.02,10.01" fill="#000000"/>'
    "</svg>"
).encode("utf-8")


def _slow_check(svg_text: str, close_gap_ratio: float, duplicate_point_ratio: float, max_subpaths: int) -> dict:
    time.sleep(0.5)
    return {"open_path_issues": [], "duplicate_issues": [], "skipped_path_count": 0}


@pytest.fixture()
def service() -> PathCheckerService:
    return PathCheckerService(Settings())


def test_process_returns_reproducible_result_for_same_input(service):
    params = CheckParams()

    first = service.process(OPEN_SQUARE_SVG, params)
    second = service.process(OPEN_SQUARE_SVG, params)

    assert first == second


def test_process_detects_the_open_path_and_reports_summary_consistently(service):
    params = CheckParams()

    result = service.process(OPEN_SQUARE_SVG, params)

    assert result.summary.open_path_count == 1
    assert result.summary.duplicate_group_count == 0
    assert len(result.issues) == 1
    assert result.issues[0].type == "open_path"


def test_process_does_not_mutate_input_bytes(service):
    original_copy = bytes(OPEN_SQUARE_SVG)
    params = CheckParams()

    service.process(OPEN_SQUARE_SVG, params)

    assert OPEN_SQUARE_SVG == original_copy


def test_process_raises_invalid_input_svg_error_for_non_utf8_bytes(service):
    params = CheckParams()

    with pytest.raises(InvalidInputSvgError):
        service.process(b"\xff\xfe\x00\x01", params)


def test_process_raises_invalid_input_svg_error_for_malformed_xml(service):
    params = CheckParams()

    with pytest.raises(InvalidInputSvgError):
        service.process(b"<svg><path d='M0,0'></svg-not-closed>", params)


def test_process_raises_invalid_input_svg_error_for_non_svg_root(service):
    params = CheckParams()

    with pytest.raises(InvalidInputSvgError):
        service.process(b'<html xmlns="http://www.w3.org/2000/svg"></html>', params)


def test_process_raises_svg_input_too_large_when_input_exceeds_configured_limit():
    tiny_input_service = PathCheckerService(Settings(max_svg_output_bytes=10))
    params = CheckParams()

    with pytest.raises(SvgInputTooLargeError):
        tiny_input_service.process(OPEN_SQUARE_SVG, params)


def test_process_raises_timeout_when_analysis_takes_too_long():
    slow_service = PathCheckerService(Settings(check_timeout_seconds=0), check_fn=_slow_check)
    params = CheckParams()

    with pytest.raises(CheckTimeoutError):
        slow_service.process(OPEN_SQUARE_SVG, params)


def test_process_returns_promptly_on_timeout_even_if_analysis_keeps_running():
    """Regresión: `_check_with_timeout` no debe bloquear esperando a que el
    hilo colgado termine -- debe retornar (lanzando CheckTimeoutError) cerca
    del timeout configurado, no del delay real de la función lenta. Mismo
    criterio que el test equivalente de SimplificationService."""

    def very_slow_check(svg_text: str, close_gap_ratio: float, duplicate_point_ratio: float, max_subpaths: int) -> dict:
        time.sleep(3.0)
        return {"open_path_issues": [], "duplicate_issues": [], "skipped_path_count": 0}

    slow_service = PathCheckerService(Settings(check_timeout_seconds=1), check_fn=very_slow_check)
    params = CheckParams()

    start = time.perf_counter()
    with pytest.raises(CheckTimeoutError):
        slow_service.process(OPEN_SQUARE_SVG, params)
    elapsed = time.perf_counter() - start

    assert elapsed < 2.0


def test_process_sanitizes_dangerous_content_before_analyzing(service):
    malicious_svg = (
        '<svg xmlns="http://www.w3.org/2000/svg" width="60" height="60">'
        "<script>alert(1)</script>"
        '<path d="M10,10 L10,20 L10,30 L50,50 L50,10 Z" onload="alert(2)"/>'
        "</svg>"
    ).encode("utf-8")
    params = CheckParams()

    # No debe lanzar -- el <script>/onload se sanean antes de analizar, y el
    # análisis en sí nunca vuelve a exponer el SVG (es de solo lectura).
    result = service.process(malicious_svg, params)

    assert result.skipped_path_count == 0


def test_process_never_mentions_the_svg_content_in_the_response():
    # El checker es de solo lectura: la respuesta nunca debería incluir un
    # campo con el SVG completo (a diferencia de SimplifyResponse) -- solo
    # índices/coordenadas de issues.
    params = CheckParams()

    result = PathCheckerService(Settings()).process(OPEN_SQUARE_SVG, params)

    assert not hasattr(result, "svg")
