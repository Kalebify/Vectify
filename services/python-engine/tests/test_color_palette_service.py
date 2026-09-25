"""Tests del servicio de orquestación (ColorPaletteService): reproducibilidad
extremo a extremo, respeto de los límites de dimensiones/timeout, manejo de
transparencia end-to-end (decodificación real de PNG con canal alfa) y que
la entrada nunca se modifica. Mismo criterio que test_threshold_service.py.
"""

import base64

import cv2
import numpy as np
import pytest

from app.core.color_palette_pipeline import PaletteDetectionResult
from app.core.config import Settings
from app.core.errors import CorruptImageError, DimensionsExceededError
from app.models.schemas import ColorPaletteParams
from app.services.color_palette_service import ColorPaletteService, ColorPaletteTimeoutError
from tests.support import (
    NOT_AN_IMAGE,
    make_antialiased_edge_png_bytes,
    make_gradient_png_bytes,
    make_half_transparent_rgba_png_bytes,
    make_near_identical_colors_png_bytes,
    make_png_bytes,
    make_rgba_png_bytes,
    make_shadow_png_bytes,
    make_solid_colors_png_bytes,
)


@pytest.fixture()
def service() -> ColorPaletteService:
    return ColorPaletteService(Settings())


def test_process_returns_reproducible_result_for_same_input_and_params(service):
    data = make_solid_colors_png_bytes()
    params = ColorPaletteParams(tolerance=10.0, max_colors=None)

    first = service.process(data, params)
    second = service.process(data, params)

    assert first.model_dump() == second.model_dump()


def test_process_solid_colors_detects_three_groups(service):
    data = make_solid_colors_png_bytes(width=12, height=12)
    params = ColorPaletteParams(tolerance=5.0, max_colors=None)

    result = service.process(data, params)

    assert result.metrics.color_count == 3
    assert len(result.groups) == 3
    total_pixels = result.width * result.height
    assert sum(g.pixel_count for g in result.groups) == total_pixels
    for group in result.groups:
        decoded = cv2.imdecode(np.frombuffer(base64.b64decode(group.mask_base64), dtype=np.uint8), cv2.IMREAD_GRAYSCALE)
        assert set(np.unique(decoded).tolist()) <= {0, 255}


def test_process_shadow_variations_merge_with_wide_tolerance(service):
    data = make_shadow_png_bytes()

    result = service.process(data, ColorPaletteParams(tolerance=60.0, max_colors=None))

    assert result.metrics.color_count == 1


def test_process_shadow_variations_stay_separate_with_strict_tolerance(service):
    data = make_shadow_png_bytes()

    result = service.process(data, ColorPaletteParams(tolerance=1.0, max_colors=None))

    assert result.metrics.color_count == 2


def test_process_near_identical_colors_merge_within_tolerance(service):
    data = make_near_identical_colors_png_bytes()

    result = service.process(data, ColorPaletteParams(tolerance=10.0, max_colors=None))

    assert result.metrics.color_count == 1


def test_process_antialiased_edge_respects_max_colors(service):
    data = make_antialiased_edge_png_bytes()

    result = service.process(data, ColorPaletteParams(tolerance=80.0, max_colors=3))

    assert result.metrics.color_count <= 3
    assert sum(g.pixel_count for g in result.groups) == result.width * result.height


def test_process_many_colors_respects_max_colors_upper_bound(service):
    data = make_gradient_png_bytes(width=32, height=32)

    result = service.process(data, ColorPaletteParams(tolerance=12.0, max_colors=6))

    assert result.metrics.color_count <= 6
    assert sum(g.pixel_count for g in result.groups) == result.width * result.height


def test_process_fully_transparent_image_produces_empty_palette_and_reports_transparent_percent(service):
    data = make_rgba_png_bytes(6, 6, color=(10, 10, 10), alpha=0)

    result = service.process(data, ColorPaletteParams(tolerance=5.0, max_colors=None))

    assert result.metrics.color_count == 0
    assert result.metrics.transparent_percent == pytest.approx(100.0)
    assert result.groups == []


def test_process_partial_transparency_does_not_fail_and_is_flagged(service):
    data = make_rgba_png_bytes(6, 6, color=(200, 200, 200), alpha=64)

    result = service.process(data, ColorPaletteParams(tolerance=5.0, max_colors=None))

    assert result.metrics.color_count == 1
    assert result.groups[0].has_partial_alpha is True
    assert result.metrics.transparent_percent == pytest.approx(0.0)


def test_process_distinguishes_fully_transparent_background_from_solid_color_of_the_same_hue(service):
    # Igual color RGB en ambas mitades: una totalmente transparente (alpha=0,
    # NO debe contar como color) y la otra totalmente opaca (SÍ debe quedar
    # como color de paleta) -- ver spec.md M2-S01, criterios de aceptación:
    # "debe poder distinguirse de un color sólido de fondo real".
    data = make_half_transparent_rgba_png_bytes(8, 8, color=(200, 200, 200))

    result = service.process(data, ColorPaletteParams(tolerance=5.0, max_colors=None))

    assert result.metrics.color_count == 1
    assert result.groups[0].pixel_count == 32  # mitad opaca de un 8x8
    assert result.metrics.transparent_percent == pytest.approx(50.0)


def test_process_quantized_preview_is_a_valid_rgba_png(service):
    data = make_solid_colors_png_bytes()

    result = service.process(data, ColorPaletteParams(tolerance=5.0, max_colors=None))

    decoded = cv2.imdecode(
        np.frombuffer(base64.b64decode(result.quantized_preview_base64), dtype=np.uint8), cv2.IMREAD_UNCHANGED
    )
    assert decoded.shape == (result.height, result.width, 4)


def test_process_does_not_mutate_input_bytes(service):
    data = make_solid_colors_png_bytes()
    original_copy = bytes(data)

    service.process(data, ColorPaletteParams(tolerance=5.0, max_colors=None))

    assert data == original_copy


def test_process_raises_corrupt_image_error_for_invalid_bytes(service):
    with pytest.raises(CorruptImageError):
        service.process(NOT_AN_IMAGE, ColorPaletteParams())


def test_process_raises_dimensions_exceeded_for_oversized_image():
    tiny_limits_service = ColorPaletteService(Settings(max_image_width=4, max_image_height=4, max_image_pixels=16))
    data = make_png_bytes(50, 50)

    with pytest.raises(DimensionsExceededError):
        tiny_limits_service.process(data, ColorPaletteParams())


def test_process_raises_timeout_error_when_detection_exceeds_budget():
    def slow_detect(*args, **kwargs) -> PaletteDetectionResult:
        import time

        time.sleep(0.2)
        return PaletteDetectionResult(width=1, height=1, total_pixel_count=1, transparent_pixel_count=0, groups=[])

    fast_timeout_service = ColorPaletteService(
        Settings(color_palette_timeout_seconds=0), detect_fn=slow_detect
    )
    data = make_png_bytes(4, 4)

    with pytest.raises(ColorPaletteTimeoutError):
        fast_timeout_service.process(data, ColorPaletteParams())
