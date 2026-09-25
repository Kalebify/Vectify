"""Tests unitarios de las funciones puras de la etapa de threshold
(app.core.threshold_pipeline): corrección del umbral binario, inversión,
determinismo y métricas de porcentaje foreground/background. Ver spec.md
M1-S04, sección "Pruebas".
"""

import numpy as np
import pytest

from app.core import threshold_pipeline
from app.core.errors import CorruptImageError
from tests.support import make_checkerboard_png_bytes, make_png_bytes, make_rgba_png_bytes
from app.core import pipeline


# --- read_image_with_alpha / split_alpha_channel ---


def test_read_image_with_alpha_preserves_4_channels_for_rgba_png():
    data = make_rgba_png_bytes(3, 3, color=(10, 20, 30), alpha=64)

    image = threshold_pipeline.read_image_with_alpha(data)

    assert image.ndim == 3
    assert image.shape[2] == 4


def test_read_image_with_alpha_raises_corrupt_image_error_for_invalid_bytes():
    with pytest.raises(CorruptImageError):
        threshold_pipeline.read_image_with_alpha(b"esto no es una imagen")


def test_read_image_with_alpha_raises_corrupt_image_error_for_empty_bytes():
    with pytest.raises(CorruptImageError):
        threshold_pipeline.read_image_with_alpha(b"")


def test_split_alpha_channel_separates_bgr_and_alpha_for_4_channel_image():
    image = np.full((2, 2, 4), (10, 20, 30, 64), dtype=np.uint8)

    bgr, alpha = threshold_pipeline.split_alpha_channel(image)

    assert bgr.shape == (2, 2, 3)
    assert (alpha == 64).all()


def test_split_alpha_channel_is_passthrough_for_3_channel_image():
    image = np.full((2, 2, 3), (10, 20, 30), dtype=np.uint8)

    bgr, alpha = threshold_pipeline.split_alpha_channel(image)

    assert bgr is image
    assert alpha is None


# --- apply_alpha_as_background ---


def test_apply_alpha_as_background_forces_fully_transparent_pixels_to_zero():
    mask = np.full((2, 2), 255, dtype=np.uint8)
    alpha = np.array([[255, 0], [255, 0]], dtype=np.uint8)

    result = threshold_pipeline.apply_alpha_as_background(mask, alpha)

    assert tuple(result[0]) == (255, 0)
    assert tuple(result[1]) == (255, 0)


def test_apply_alpha_as_background_is_passthrough_when_alpha_is_none():
    mask = np.full((2, 2), 255, dtype=np.uint8)

    result = threshold_pipeline.apply_alpha_as_background(mask, None)

    assert result is mask


def test_apply_alpha_as_background_does_not_mutate_input_mask():
    mask = np.full((2, 2), 255, dtype=np.uint8)
    alpha = np.zeros((2, 2), dtype=np.uint8)

    threshold_pipeline.apply_alpha_as_background(mask, alpha)

    assert (mask == 255).all()


# --- to_grayscale_single_channel ---


def test_to_grayscale_single_channel_passthrough_for_2d_array():
    gray = np.zeros((4, 4), dtype=np.uint8)

    result = threshold_pipeline.to_grayscale_single_channel(gray)

    assert result is gray


def test_to_grayscale_single_channel_reduces_3_channel_image():
    image = np.full((2, 2, 3), (10, 20, 30), dtype=np.uint8)

    result = threshold_pipeline.to_grayscale_single_channel(image)

    assert result.ndim == 2
    assert result.shape == (2, 2)


# --- apply_threshold ---


def test_apply_threshold_pixels_above_value_become_white():
    gray = np.array([[50, 200]], dtype=np.uint8)

    result = threshold_pipeline.apply_threshold(gray, value=128, invert=False)

    assert tuple(result[0]) == (0, 255)


def test_apply_threshold_invert_flips_the_result():
    gray = np.array([[50, 200]], dtype=np.uint8)

    result = threshold_pipeline.apply_threshold(gray, value=128, invert=True)

    assert tuple(result[0]) == (255, 0)


def test_apply_threshold_value_zero_makes_everything_white_except_pure_black():
    gray = np.array([[0, 1, 255]], dtype=np.uint8)

    result = threshold_pipeline.apply_threshold(gray, value=0, invert=False)

    assert tuple(result[0]) == (0, 255, 255)


def test_apply_threshold_value_255_makes_everything_black():
    gray = np.array([[0, 128, 255]], dtype=np.uint8)

    result = threshold_pipeline.apply_threshold(gray, value=255, invert=False)

    assert tuple(result[0]) == (0, 0, 0)


def test_apply_threshold_is_deterministic_across_repeated_calls():
    image = pipeline.read_image_safely(make_checkerboard_png_bytes(size=8))
    gray = threshold_pipeline.to_grayscale_single_channel(image)

    first = threshold_pipeline.apply_threshold(gray, value=100, invert=False)
    second = threshold_pipeline.apply_threshold(gray, value=100, invert=False)

    assert np.array_equal(first, second)


# --- compute_threshold_metrics ---


def test_compute_threshold_metrics_all_white_is_100_percent_foreground():
    mask = np.full((4, 4), 255, dtype=np.uint8)

    metrics = threshold_pipeline.compute_threshold_metrics(mask)

    assert metrics["foreground_percent"] == pytest.approx(100.0)
    assert metrics["background_percent"] == pytest.approx(0.0)


def test_compute_threshold_metrics_all_black_is_0_percent_foreground():
    mask = np.zeros((4, 4), dtype=np.uint8)

    metrics = threshold_pipeline.compute_threshold_metrics(mask)

    assert metrics["foreground_percent"] == pytest.approx(0.0)
    assert metrics["background_percent"] == pytest.approx(100.0)


def test_compute_threshold_metrics_checkerboard_is_roughly_half():
    mask = np.zeros((8, 8), dtype=np.uint8)
    mask[::2, ::2] = 255
    mask[1::2, 1::2] = 255

    metrics = threshold_pipeline.compute_threshold_metrics(mask)

    assert metrics["foreground_percent"] == pytest.approx(50.0)
    assert metrics["background_percent"] == pytest.approx(50.0)


def test_full_pipeline_produces_expected_mask_for_light_image():
    # Imagen clara (240,240,240 en BGR): con el umbral por defecto (128) debe
    # quedar casi toda blanca (foreground).
    data = make_png_bytes(4, 4, color=(240, 240, 240))
    image = pipeline.read_image_safely(data)
    gray = threshold_pipeline.to_grayscale_single_channel(image)

    mask = threshold_pipeline.apply_threshold(gray, value=128, invert=False)
    metrics = threshold_pipeline.compute_threshold_metrics(mask)

    assert metrics["foreground_percent"] == pytest.approx(100.0)


def test_full_pipeline_produces_expected_mask_for_dark_image():
    # Imagen oscura (10,10,10 en BGR): con el umbral por defecto (128) debe
    # quedar toda negra (background).
    data = make_png_bytes(4, 4, color=(10, 10, 10))
    image = pipeline.read_image_safely(data)
    gray = threshold_pipeline.to_grayscale_single_channel(image)

    mask = threshold_pipeline.apply_threshold(gray, value=128, invert=False)
    metrics = threshold_pipeline.compute_threshold_metrics(mask)

    assert metrics["foreground_percent"] == pytest.approx(0.0)
