"""Tests del servicio de orquestación (VectorizationService): reproducibilidad
extremo a extremo (dentro de lo documentado -- ver reporte del sprint),
respeto de los límites de dimensiones/tamaño de salida, rechazo controlado de
máscaras vacías y timeout del motor de trazado (inyectado como fake, sin
depender de que VTracer realmente tarde). Mismo criterio que
test_threshold_service.py.
"""

import time

import pytest

from app.core import pipeline
from app.core.config import Settings
from app.core.errors import (
    CorruptImageError,
    DimensionsExceededError,
    EmptyMaskError,
    SvgOutputTooLargeError,
    VectorizationTimeoutError,
)
from app.services.vectorization_service import VectorizationService
from tests.support import NOT_AN_IMAGE, make_mask_png_bytes, make_ring_mask_png_bytes, make_square_mask_png_bytes


class FakeEngine:
    """VectorEngine en memoria: permite forzar una respuesta fija o simular
    que el motor tarda (para probar el timeout sin depender de que VTracer
    realmente sea lento)."""

    def __init__(self, svg: str = '<svg xmlns="http://www.w3.org/2000/svg" width="1" height="1"></svg>', delay: float = 0.0):
        self.svg = svg
        self.delay = delay
        self.call_count = 0

    def trace(self, mask):
        self.call_count += 1
        if self.delay:
            time.sleep(self.delay)
        return self.svg


SQUARE_SVG = (
    '<svg xmlns="http://www.w3.org/2000/svg" width="60" height="60">'
    '<path d="M15,15 L45,15 L45,45 L15,45 Z" fill="#000000"/>'
    "</svg>"
)


@pytest.fixture()
def service() -> VectorizationService:
    return VectorizationService(Settings(), engine=FakeEngine(svg=SQUARE_SVG))


def test_process_returns_reproducible_result_for_same_input(service):
    data = make_square_mask_png_bytes()

    first = service.process(data)
    second = service.process(data)

    assert first.svg == second.svg
    assert first.metrics == second.metrics
    assert first.width == second.width
    assert first.height == second.height


def test_process_returns_expected_stats_for_simple_square(service):
    data = make_square_mask_png_bytes(size=60, square=30)

    result = service.process(data)

    assert result.metrics.path_count == 1
    assert result.metrics.approx_node_count == 4
    assert result.content_type == "image/svg+xml"


def test_process_does_not_mutate_input_bytes(service):
    data = make_square_mask_png_bytes()
    original_copy = bytes(data)

    service.process(data)

    assert data == original_copy


def test_process_raises_corrupt_image_error_for_invalid_bytes(service):
    with pytest.raises(CorruptImageError):
        service.process(NOT_AN_IMAGE)


def test_process_raises_empty_mask_error_for_all_background_mask(service):
    data = make_mask_png_bytes(20, 20)  # todo negro: sin foreground

    with pytest.raises(EmptyMaskError):
        service.process(data)


def test_process_raises_dimensions_exceeded_for_oversized_image():
    tiny_limits_service = VectorizationService(
        Settings(max_image_width=4, max_image_height=4, max_image_pixels=16),
        engine=FakeEngine(svg=SQUARE_SVG),
    )
    data = make_square_mask_png_bytes(size=50, square=20)

    with pytest.raises(DimensionsExceededError):
        tiny_limits_service.process(data)


def test_process_rejects_oversized_image_by_header_without_full_decode(monkeypatch):
    tiny_limits_service = VectorizationService(
        Settings(max_image_width=4, max_image_height=4, max_image_pixels=16),
        engine=FakeEngine(svg=SQUARE_SVG),
    )
    data = make_square_mask_png_bytes(size=50, square=20)

    def fail_if_called(*args, **kwargs):
        raise AssertionError(
            "read_image_safely no debería llamarse: el chequeo por cabecera "
            "tiene que rechazar la imagen antes de decodificarla por completo."
        )

    monkeypatch.setattr(pipeline, "read_image_safely", fail_if_called)

    with pytest.raises(DimensionsExceededError):
        tiny_limits_service.process(data)


def test_process_raises_timeout_when_engine_takes_too_long():
    slow_service = VectorizationService(
        Settings(vectorize_timeout_seconds=0),
        engine=FakeEngine(svg=SQUARE_SVG, delay=0.5),
    )
    data = make_square_mask_png_bytes()

    with pytest.raises(VectorizationTimeoutError):
        slow_service.process(data)


def test_process_returns_promptly_on_timeout_even_if_engine_keeps_running():
    """Regresión: `_trace_with_timeout` no debe bloquear esperando a que el
    hilo colgado del engine termine -- debe retornar (lanzando
    VectorizationTimeoutError) cerca del timeout configurado y no del delay
    real del engine. `vectorize_timeout_seconds` es int (ver Settings), así
    que se usa 1s de timeout contra un engine que tarda 3s: con el bug de
    `with ThreadPoolExecutor(...)` (shutdown(wait=True) en el camino de
    timeout) esto tardaría ~3s; con el fix, ~1s."""
    slow_service = VectorizationService(
        Settings(vectorize_timeout_seconds=1),
        engine=FakeEngine(svg=SQUARE_SVG, delay=3.0),
    )
    data = make_square_mask_png_bytes()

    start = time.perf_counter()
    with pytest.raises(VectorizationTimeoutError):
        slow_service.process(data)
    elapsed = time.perf_counter() - start

    assert elapsed < 2.0


def test_process_raises_svg_output_too_large_when_result_exceeds_configured_limit():
    huge_svg = (
        '<svg xmlns="http://www.w3.org/2000/svg" width="1" height="1">'
        + "".join(f'<path d="M{i},{i} L{i + 1},{i} Z"/>' for i in range(2000))
        + "</svg>"
    )
    tiny_output_service = VectorizationService(
        Settings(max_svg_output_bytes=200),
        engine=FakeEngine(svg=huge_svg),
    )
    data = make_square_mask_png_bytes()

    with pytest.raises(SvgOutputTooLargeError):
        tiny_output_service.process(data)


def test_process_sanitizes_dangerous_content_from_the_engines_raw_svg():
    malicious_svg = (
        '<svg xmlns="http://www.w3.org/2000/svg" width="10" height="10">'
        "<script>alert(1)</script>"
        '<path d="M0,0 L10,0 L10,10 L0,10 Z" onload="alert(2)"/>'
        "</svg>"
    )
    service_with_malicious_engine = VectorizationService(Settings(), engine=FakeEngine(svg=malicious_svg))
    data = make_square_mask_png_bytes()

    result = service_with_malicious_engine.process(data)

    assert "<script" not in result.svg
    assert "onload" not in result.svg
    assert "<path" in result.svg


def test_process_with_ring_mask_reports_single_path_with_more_nodes_than_a_square():
    ring_svg = (
        '<svg xmlns="http://www.w3.org/2000/svg" width="80" height="80">'
        '<path d="M10,10 L70,10 L70,70 L10,70 Z M30,30 L50,30 L50,50 L30,50 Z" fill="#000000"/>'
        "</svg>"
    )
    service_with_ring_engine = VectorizationService(Settings(), engine=FakeEngine(svg=ring_svg))
    data = make_ring_mask_png_bytes()

    result = service_with_ring_engine.process(data)

    assert result.metrics.path_count == 1
    assert result.metrics.approx_node_count == 8
