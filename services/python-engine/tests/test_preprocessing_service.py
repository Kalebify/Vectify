"""Tests del servicio de orquestación (PreprocessingService): reproducibilidad
extremo a extremo, respeto de los límites de dimensiones y que el original
(los bytes de entrada) nunca se modifica.
"""

import pytest

from app.core import pipeline
from app.core.config import Settings
from app.core.errors import CorruptImageError, DimensionsExceededError
from app.models.schemas import PreprocessParams
from app.services.preprocessing_service import PreprocessingService
from tests.support import NOT_AN_IMAGE, make_checkerboard_png_bytes, make_png_bytes


@pytest.fixture()
def service() -> PreprocessingService:
    return PreprocessingService(Settings())


def test_process_returns_reproducible_result_for_same_input_and_params(service):
    data = make_checkerboard_png_bytes(size=8)
    params = PreprocessParams(grayscale=True, contrast=1.4, brightness=15, denoise=3)

    first = service.process(data, params)
    second = service.process(data, params)

    assert first.image_base64 == second.image_base64
    assert first.metrics == second.metrics
    assert first.width == second.width
    assert first.height == second.height


def test_process_with_default_params_does_not_change_dimensions(service):
    data = make_png_bytes(12, 9, color=(5, 6, 7))
    params = PreprocessParams()

    result = service.process(data, params)

    assert result.width == 12
    assert result.height == 9
    assert result.original_width == 12
    assert result.original_height == 9
    assert result.effective_params == params


def test_process_does_not_mutate_input_bytes(service):
    data = make_png_bytes(4, 4, color=(9, 9, 9))
    original_copy = bytes(data)
    params = PreprocessParams(grayscale=True, contrast=2.0, brightness=-20, denoise=5)

    service.process(data, params)

    # bytes es inmutable en Python, pero esto documenta explícitamente la
    # garantía: el pipeline nunca debe requerir (ni intentar) escribir sobre
    # el buffer recibido.
    assert data == original_copy


def test_process_raises_corrupt_image_error_for_invalid_bytes(service):
    with pytest.raises(CorruptImageError):
        service.process(NOT_AN_IMAGE, PreprocessParams())


def test_process_raises_dimensions_exceeded_for_oversized_image():
    tiny_limits_service = PreprocessingService(
        Settings(max_image_width=4, max_image_height=4, max_image_pixels=16)
    )
    data = make_png_bytes(50, 50)

    with pytest.raises(DimensionsExceededError):
        tiny_limits_service.process(data, PreprocessParams())


def test_process_rejects_oversized_image_by_header_without_full_decode(monkeypatch):
    # Defensa contra bombas de descompresión: el chequeo de dimensiones por
    # cabecera debe rechazar la imagen ANTES de que se llame a
    # pipeline.read_image_safely (que decodificaría todos los píxeles). Se
    # fuerza el fallo del test si esa función llega a invocarse.
    tiny_limits_service = PreprocessingService(
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
        tiny_limits_service.process(data, PreprocessParams())
