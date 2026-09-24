"""Tests del servicio de orquestación (ThresholdingService): reproducibilidad
extremo a extremo, respeto de los límites de dimensiones y que la entrada
(los bytes recibidos) nunca se modifica. Mismo criterio que
test_preprocessing_service.py.
"""

import pytest

from app.core import pipeline
from app.core.config import Settings
from app.core.errors import CorruptImageError, DimensionsExceededError
from app.models.schemas import ThresholdParams
from app.services.threshold_service import ThresholdingService
from tests.support import NOT_AN_IMAGE, make_png_bytes, make_rgba_png_bytes


@pytest.fixture()
def service() -> ThresholdingService:
    return ThresholdingService(Settings())


def test_process_returns_reproducible_result_for_same_input_and_params(service):
    data = make_png_bytes(8, 8, color=(90, 90, 90))
    params = ThresholdParams(value=100, invert=False)

    first = service.process(data, params)
    second = service.process(data, params)

    assert first.image_base64 == second.image_base64
    assert first.metrics == second.metrics
    assert first.width == second.width
    assert first.height == second.height


def test_process_with_light_image_is_mostly_foreground(service):
    data = make_png_bytes(6, 6, color=(250, 250, 250))
    params = ThresholdParams(value=128, invert=False)

    result = service.process(data, params)

    assert result.metrics.foreground_percent == pytest.approx(100.0)
    assert result.metrics.background_percent == pytest.approx(0.0)


def test_process_with_dark_image_is_mostly_background(service):
    data = make_png_bytes(6, 6, color=(5, 5, 5))
    params = ThresholdParams(value=128, invert=False)

    result = service.process(data, params)

    assert result.metrics.foreground_percent == pytest.approx(0.0)
    assert result.metrics.background_percent == pytest.approx(100.0)


def test_process_with_transparent_image_does_not_fail(service):
    data = make_rgba_png_bytes(6, 6, color=(200, 200, 200), alpha=64)
    params = ThresholdParams(value=128, invert=False)

    result = service.process(data, params)

    assert result.width == 6
    assert result.height == 6


def test_process_invert_flips_which_side_is_foreground(service):
    data = make_png_bytes(4, 4, color=(200, 200, 200))

    normal = service.process(data, ThresholdParams(value=128, invert=False))
    inverted = service.process(data, ThresholdParams(value=128, invert=True))

    assert normal.metrics.foreground_percent == pytest.approx(100.0)
    assert inverted.metrics.foreground_percent == pytest.approx(0.0)


def test_process_produces_empty_mask_when_value_is_255_without_invert(service):
    data = make_png_bytes(4, 4, color=(200, 200, 200))
    params = ThresholdParams(value=255, invert=False)

    result = service.process(data, params)

    assert result.metrics.foreground_percent == pytest.approx(0.0)


def test_process_produces_full_mask_when_value_is_255_with_invert(service):
    data = make_png_bytes(4, 4, color=(200, 200, 200))
    params = ThresholdParams(value=255, invert=True)

    result = service.process(data, params)

    assert result.metrics.foreground_percent == pytest.approx(100.0)


def test_process_does_not_mutate_input_bytes(service):
    data = make_png_bytes(4, 4, color=(9, 9, 9))
    original_copy = bytes(data)

    service.process(data, ThresholdParams(value=128, invert=False))

    assert data == original_copy


def test_process_raises_corrupt_image_error_for_invalid_bytes(service):
    with pytest.raises(CorruptImageError):
        service.process(NOT_AN_IMAGE, ThresholdParams())


def test_process_raises_dimensions_exceeded_for_oversized_image():
    tiny_limits_service = ThresholdingService(
        Settings(max_image_width=4, max_image_height=4, max_image_pixels=16)
    )
    data = make_png_bytes(50, 50)

    with pytest.raises(DimensionsExceededError):
        tiny_limits_service.process(data, ThresholdParams())


def test_process_rejects_oversized_image_by_header_without_full_decode(monkeypatch):
    tiny_limits_service = ThresholdingService(
        Settings(max_image_width=4, max_image_height=4, max_image_pixels=16)
    )
    data = make_png_bytes(50, 50)

    def fail_if_called(*args, **kwargs):
        raise AssertionError(
            "read_image_safely no debería llamarse: el chequeo por cabecera "
            "tiene que rechazar la imagen antes de decodificarla por completo."
        )

    monkeypatch.setattr(pipeline, "read_image_safely", fail_if_called)

    with pytest.raises(DimensionsExceededError):
        tiny_limits_service.process(data, ThresholdParams())
