"""Tests unitarios de las funciones puras de la etapa de threshold
(app.core.threshold_pipeline): corrección del umbral binario, inversión,
determinismo y métricas de porcentaje foreground/background. Ver spec.md
M1-S04, sección "Pruebas".
"""

import numpy as np
import pytest

from app.core import threshold_pipeline
from tests.support import make_checkerboard_png_bytes, make_png_bytes
from app.core import pipeline


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
