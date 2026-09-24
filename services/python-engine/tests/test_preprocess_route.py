"""Tests de integración de POST /api/v1/preprocess vía TestClient: contrato de
respuesta, reproducibilidad extremo a extremo, límites de sliders y errores
controlados (imagen corrupta, dimensiones excesivas, parámetros inválidos).
"""

import base64
import json

import cv2
import numpy as np
import pytest

from app.core.config import Settings, get_settings
from app.main import app
from tests.support import NOT_AN_IMAGE, make_checkerboard_png_bytes, make_png_bytes

PARAMS_URL = "/api/v1/preprocess"


def _post_preprocess(client, image_bytes: bytes, params: dict, filename: str = "sample.png"):
    return client.post(
        PARAMS_URL,
        files={"file": (filename, image_bytes, "image/png")},
        data={"params": json.dumps(params)},
    )


def test_preprocess_returns_expected_contract(client):
    image_bytes = make_png_bytes(32, 16, color=(40, 80, 120))
    params = {"grayscale": True, "contrast": 1.5, "brightness": 10, "denoise": 2}

    response = _post_preprocess(client, image_bytes, params)

    assert response.status_code == 200
    body = response.json()
    assert body["width"] == 32
    assert body["height"] == 16
    assert body["original_width"] == 32
    assert body["original_height"] == 16
    assert body["effective_params"] == params
    assert body["content_type"] == "image/png"
    assert set(body["metrics"].keys()) == {"mean_brightness", "std_dev", "min_value", "max_value"}

    decoded_bytes = base64.b64decode(body["image_base64"])
    decoded_image = cv2.imdecode(np.frombuffer(decoded_bytes, dtype=np.uint8), cv2.IMREAD_COLOR)
    assert decoded_image.shape == (16, 32, 3)


def test_preprocess_is_deterministic_across_repeated_requests(client):
    image_bytes = make_checkerboard_png_bytes(size=8)
    params = {"grayscale": False, "contrast": 1.2, "brightness": -5, "denoise": 4}

    first = _post_preprocess(client, image_bytes, params)
    second = _post_preprocess(client, image_bytes, params)

    assert first.status_code == second.status_code == 200
    assert first.json()["image_base64"] == second.json()["image_base64"]
    assert first.json()["metrics"] == second.json()["metrics"]


def test_preprocess_with_default_params_preserves_pixels(client):
    image = np.zeros((5, 5, 3), dtype=np.uint8)
    image[:] = (30, 60, 90)
    image[2, 2] = (200, 10, 5)
    success, buffer = cv2.imencode(".png", image)
    assert success

    response = _post_preprocess(client, buffer.tobytes(), {"grayscale": False, "contrast": 1.0, "brightness": 0, "denoise": 0})

    assert response.status_code == 200
    decoded_bytes = base64.b64decode(response.json()["image_base64"])
    decoded_image = cv2.imdecode(np.frombuffer(decoded_bytes, dtype=np.uint8), cv2.IMREAD_COLOR)
    assert np.array_equal(decoded_image, image)


def test_preprocess_rejects_corrupt_file(client):
    response = _post_preprocess(client, NOT_AN_IMAGE, {"grayscale": False, "contrast": 1.0, "brightness": 0, "denoise": 0})

    assert response.status_code == 400
    body = response.json()
    assert body["code"] == "corrupt_image"


@pytest.mark.parametrize(
    "params",
    [
        {"grayscale": False, "contrast": 0.49, "brightness": 0, "denoise": 0},
        {"grayscale": False, "contrast": 3.01, "brightness": 0, "denoise": 0},
        {"grayscale": False, "contrast": 1.0, "brightness": -101, "denoise": 0},
        {"grayscale": False, "contrast": 1.0, "brightness": 101, "denoise": 0},
        {"grayscale": False, "contrast": 1.0, "brightness": 0, "denoise": -1},
        {"grayscale": False, "contrast": 1.0, "brightness": 0, "denoise": 11},
    ],
)
def test_preprocess_rejects_out_of_range_parameters(client, params):
    image_bytes = make_png_bytes(4, 4)

    response = _post_preprocess(client, image_bytes, params)

    assert response.status_code == 422
    assert response.json()["code"] == "invalid_parameters"


@pytest.mark.parametrize(
    "params",
    [
        {"grayscale": False, "contrast": 0.5, "brightness": -100, "denoise": 0},
        {"grayscale": False, "contrast": 3.0, "brightness": 100, "denoise": 10},
    ],
)
def test_preprocess_accepts_boundary_slider_values(client, params):
    image_bytes = make_png_bytes(4, 4)

    response = _post_preprocess(client, image_bytes, params)

    assert response.status_code == 200
    assert response.json()["effective_params"] == params


def test_preprocess_rejects_malformed_params_json(client):
    image_bytes = make_png_bytes(4, 4)

    response = client.post(
        PARAMS_URL,
        files={"file": ("sample.png", image_bytes, "image/png")},
        data={"params": "no es json"},
    )

    assert response.status_code == 422
    assert response.json()["code"] == "invalid_parameters"


def test_preprocess_rejects_oversized_image():
    from fastapi.testclient import TestClient

    app.dependency_overrides[get_settings] = lambda: Settings(
        max_image_width=4, max_image_height=4, max_image_pixels=16
    )
    try:
        with TestClient(app) as tiny_client:
            image_bytes = make_png_bytes(50, 50)
            response = _post_preprocess(tiny_client, image_bytes, {"grayscale": False, "contrast": 1.0, "brightness": 0, "denoise": 0})

            assert response.status_code == 413
            assert response.json()["code"] == "dimensions_exceeded"
    finally:
        app.dependency_overrides.pop(get_settings, None)
