"""Tests unitarios de las funciones puras del pipeline (app.core.pipeline):
determinismo, corrección de cada transformación por separado y los errores
controlados de lectura/límites de dimensiones. Ver spec.md M1-S03, sección
"Pruebas": "Golden images pequeñas; mismos parámetros -> mismo resultado;
límites de sliders".
"""

import numpy as np
import pytest

from app.core import pipeline
from app.core.errors import CorruptImageError, DimensionsExceededError
from tests.support import NOT_AN_IMAGE, make_checkerboard_png_bytes, make_png_bytes


# --- read_image_safely ---


def test_read_image_safely_decodes_valid_png():
    png_bytes = make_png_bytes(4, 3, color=(10, 20, 30))

    image = pipeline.read_image_safely(png_bytes)

    assert image.shape == (3, 4, 3)


def test_read_image_safely_raises_corrupt_image_error_for_garbage_bytes():
    with pytest.raises(CorruptImageError):
        pipeline.read_image_safely(NOT_AN_IMAGE)


def test_read_image_safely_raises_corrupt_image_error_for_empty_bytes():
    with pytest.raises(CorruptImageError):
        pipeline.read_image_safely(b"")


# --- check_dimensions ---


def test_check_dimensions_within_limits_does_not_raise():
    image = np.zeros((10, 20, 3), dtype=np.uint8)

    pipeline.check_dimensions(image, max_width=100, max_height=100, max_pixels=10_000)


def test_check_dimensions_raises_when_width_exceeds_limit():
    image = np.zeros((10, 200, 3), dtype=np.uint8)

    with pytest.raises(DimensionsExceededError):
        pipeline.check_dimensions(image, max_width=100, max_height=100, max_pixels=1_000_000)


def test_check_dimensions_raises_when_height_exceeds_limit():
    image = np.zeros((200, 10, 3), dtype=np.uint8)

    with pytest.raises(DimensionsExceededError):
        pipeline.check_dimensions(image, max_width=100, max_height=100, max_pixels=1_000_000)


def test_check_dimensions_raises_when_total_pixel_budget_exceeds_limit():
    image = np.zeros((90, 90, 3), dtype=np.uint8)  # 8100 px, cada dimensión OK sola

    with pytest.raises(DimensionsExceededError):
        pipeline.check_dimensions(image, max_width=100, max_height=100, max_pixels=1_000)


# --- to_grayscale ---


def test_to_grayscale_disabled_returns_same_array_unchanged():
    image = np.full((2, 2, 3), (10, 20, 30), dtype=np.uint8)

    result = pipeline.to_grayscale(image, enabled=False)

    assert np.array_equal(result, image)


def test_to_grayscale_enabled_converts_black_and_white_correctly():
    image = np.zeros((1, 2, 3), dtype=np.uint8)
    image[0, 1] = (255, 255, 255)

    result = pipeline.to_grayscale(image, enabled=True)

    assert result.shape == (1, 2, 3)
    assert tuple(result[0, 0]) == (0, 0, 0)
    assert tuple(result[0, 1]) == (255, 255, 255)


# --- adjust_contrast_brightness ---


def test_adjust_contrast_brightness_identity_returns_same_array():
    image = np.full((2, 2, 3), 100, dtype=np.uint8)

    result = pipeline.adjust_contrast_brightness(image, contrast=1.0, brightness=0)

    assert np.array_equal(result, image)


def test_adjust_contrast_brightness_applies_linear_formula():
    image = np.full((2, 2, 3), 100, dtype=np.uint8)

    result = pipeline.adjust_contrast_brightness(image, contrast=2.0, brightness=10)

    # saturate_cast(2.0 * 100 + 10) = 210
    assert np.all(result == 210)


def test_adjust_contrast_brightness_saturates_at_255():
    image = np.full((2, 2, 3), 200, dtype=np.uint8)

    result = pipeline.adjust_contrast_brightness(image, contrast=3.0, brightness=100)

    assert np.all(result == 255)


def test_adjust_contrast_brightness_negative_brightness_clips_to_zero_without_abs():
    # Regresión: cv2.convertScaleAbs aplicaba valor absoluto ANTES de saturar,
    # así que un píxel negro con brightness=-100 daba |0 - 100| = 100 en vez
    # de clippear a 0. La fórmula correcta es clip(contrast*in + brightness, 0, 255).
    image = np.zeros((2, 2, 3), dtype=np.uint8)

    result = pipeline.adjust_contrast_brightness(image, contrast=1.0, brightness=-100)

    assert np.all(result == 0)
    assert not np.all(result == 100)


# --- denoise ---


def test_denoise_zero_returns_same_array_unchanged():
    image = np.full((5, 5, 3), 128, dtype=np.uint8)

    result = pipeline.denoise(image, strength=0)

    assert np.array_equal(result, image)


def test_denoise_smooths_checkerboard_pattern():
    png_bytes = make_checkerboard_png_bytes(size=8)
    image = pipeline.read_image_safely(png_bytes)

    result = pipeline.denoise(image, strength=2)

    assert result.shape == image.shape
    # El blur promedia vecinos: un pixel interior deja de ser puro 0 o 255.
    interior = result[3, 3]
    assert not np.array_equal(interior, image[3, 3])


def test_denoise_is_deterministic_across_repeated_calls():
    image = pipeline.read_image_safely(make_checkerboard_png_bytes(size=8))

    first = pipeline.denoise(image, strength=3)
    second = pipeline.denoise(image, strength=3)

    assert np.array_equal(first, second)


# --- compute_metrics ---


def test_compute_metrics_on_uniform_image_has_zero_std_dev():
    image = np.full((10, 10, 3), 100, dtype=np.uint8)

    metrics = pipeline.compute_metrics(image)

    assert metrics["mean_brightness"] == pytest.approx(100.0)
    assert metrics["std_dev"] == pytest.approx(0.0)
    assert metrics["min_value"] == 100
    assert metrics["max_value"] == 100


def test_compute_metrics_on_checkerboard_has_min_zero_max_255():
    image = pipeline.read_image_safely(make_checkerboard_png_bytes(size=8))

    metrics = pipeline.compute_metrics(image)

    assert metrics["min_value"] == 0
    assert metrics["max_value"] == 255


# --- encode_png ---


def test_encode_png_is_deterministic_byte_for_byte():
    image = pipeline.read_image_safely(make_checkerboard_png_bytes(size=8))

    first = pipeline.encode_png(image)
    second = pipeline.encode_png(image)

    assert first == second


def test_encode_png_roundtrip_preserves_pixels():
    image = pipeline.read_image_safely(make_png_bytes(6, 5, color=(1, 2, 3)))

    encoded = pipeline.encode_png(image)
    decoded = pipeline.read_image_safely(encoded)

    assert np.array_equal(decoded, image)
