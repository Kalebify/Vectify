"""Tests del servicio de orquestación (SimplificationService): reproducibilidad,
respeto del límite de tamaño de entrada, rechazo controlado de SVG de entrada
inválido/corrupto y timeout de la simplificación (inyectada como fake, sin
depender de que Douglas-Peucker real tarde). Mismo criterio que
test_vectorization_service.py.
"""

import time

import pytest

from app.core.config import Settings
from app.core.errors import InvalidInputSvgError, SimplificationTimeoutError, SvgInputTooLargeError
from app.models.schemas import SimplifyParams
from app.services.simplification_service import SimplificationService

SQUARE_SVG = (
    '<svg xmlns="http://www.w3.org/2000/svg" width="60" height="60">'
    '<path d="M10,10 L10,20 L10,30 L10,40 L10,50 L50,50 L50,10 Z" fill="#000000"/>'
    "</svg>"
).encode("utf-8")


def _slow_simplify(svg_text: str, epsilon_ratio: float) -> str:
    time.sleep(0.5)
    return svg_text


@pytest.fixture()
def service() -> SimplificationService:
    return SimplificationService(Settings())


def test_process_returns_reproducible_result_for_same_input(service):
    params = SimplifyParams(epsilon_ratio=0.02)

    first = service.process(SQUARE_SVG, params)
    second = service.process(SQUARE_SVG, params)

    assert first.svg == second.svg
    assert first.metrics == second.metrics


def test_process_reduces_reported_node_count(service):
    params = SimplifyParams(epsilon_ratio=0.05)

    result = service.process(SQUARE_SVG, params)

    assert result.metrics.after.approx_node_count < result.metrics.before.approx_node_count
    assert result.metrics.reduction_percent > 0


def test_process_reports_before_after_and_reduction_consistently(service):
    params = SimplifyParams(epsilon_ratio=0.05)

    result = service.process(SQUARE_SVG, params)

    before = result.metrics.before.approx_node_count
    after = result.metrics.after.approx_node_count
    expected_reduction = (before - after) / before * 100.0
    assert result.metrics.reduction_percent == pytest.approx(expected_reduction)


def test_process_does_not_mutate_input_bytes(service):
    original_copy = bytes(SQUARE_SVG)
    params = SimplifyParams(epsilon_ratio=0.02)

    service.process(SQUARE_SVG, params)

    assert SQUARE_SVG == original_copy


def test_process_raises_invalid_input_svg_error_for_non_utf8_bytes(service):
    params = SimplifyParams(epsilon_ratio=0.02)

    with pytest.raises(InvalidInputSvgError):
        service.process(b"\xff\xfe\x00\x01", params)


def test_process_raises_invalid_input_svg_error_for_malformed_xml(service):
    params = SimplifyParams(epsilon_ratio=0.02)

    with pytest.raises(InvalidInputSvgError):
        service.process(b"<svg><path d='M0,0'></svg-not-closed>", params)


def test_process_raises_invalid_input_svg_error_for_non_svg_root(service):
    params = SimplifyParams(epsilon_ratio=0.02)

    with pytest.raises(InvalidInputSvgError):
        service.process(b'<html xmlns="http://www.w3.org/2000/svg"></html>', params)


def test_process_raises_svg_input_too_large_when_input_exceeds_configured_limit():
    tiny_input_service = SimplificationService(Settings(max_svg_output_bytes=10))
    params = SimplifyParams(epsilon_ratio=0.02)

    with pytest.raises(SvgInputTooLargeError):
        tiny_input_service.process(SQUARE_SVG, params)


def test_process_raises_timeout_when_simplification_takes_too_long():
    slow_service = SimplificationService(Settings(simplify_timeout_seconds=0), simplify_fn=_slow_simplify)
    params = SimplifyParams(epsilon_ratio=0.02)

    with pytest.raises(SimplificationTimeoutError):
        slow_service.process(SQUARE_SVG, params)


def test_process_returns_promptly_on_timeout_even_if_simplification_keeps_running():
    """Regresión: `_simplify_with_timeout` no debe bloquear esperando a que el
    hilo colgado termine -- debe retornar (lanzando SimplificationTimeoutError)
    cerca del timeout configurado, no del delay real de la función lenta.
    Mismo criterio que el test equivalente de VectorizationService."""

    def very_slow_simplify(svg_text: str, epsilon_ratio: float) -> str:
        time.sleep(3.0)
        return svg_text

    slow_service = SimplificationService(Settings(simplify_timeout_seconds=1), simplify_fn=very_slow_simplify)
    params = SimplifyParams(epsilon_ratio=0.02)

    start = time.perf_counter()
    with pytest.raises(SimplificationTimeoutError):
        slow_service.process(SQUARE_SVG, params)
    elapsed = time.perf_counter() - start

    assert elapsed < 2.0


def test_process_sanitizes_dangerous_content_from_the_input_svg(service):
    malicious_svg = (
        '<svg xmlns="http://www.w3.org/2000/svg" width="60" height="60">'
        "<script>alert(1)</script>"
        '<path d="M10,10 L10,20 L10,30 L50,50 L50,10 Z" onload="alert(2)"/>'
        "</svg>"
    ).encode("utf-8")
    params = SimplifyParams(epsilon_ratio=0.02)

    result = service.process(malicious_svg, params)

    assert "<script" not in result.svg
    assert "onload" not in result.svg
    assert "<path" in result.svg
