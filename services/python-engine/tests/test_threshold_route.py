"""Tests de integración de POST /api/v1/threshold vía TestClient: contrato de
respuesta, límites de valor de umbral y los casos de prueba obligatorios de
spec.md M1-S04 ("Pruebas"): imágenes claras/oscuras, transparencias, máscara
vacía, máscara completa y repetibilidad.
"""

import base64
import json

import cv2
import numpy as np
import pytest

from app.core.config import Settings, get_settings
from app.main import app
from tests.support import (
    NOT_AN_IMAGE,
    make_half_transparent_rgba_png_bytes,
    make_png_bytes,
    make_rgba_png_bytes,
)

THRESHOLD_URL = "/api/v1/threshold"


def _post_threshold(client, image_bytes: bytes, params: dict, filename: str = "preview.png"):
    return client.post(
        THRESHOLD_URL,
        files={"file": (filename, image_bytes, "image/png")},
        data={"params": json.dumps(params)},
    )


def test_threshold_returns_expected_contract(client):
    image_bytes = make_png_bytes(32, 16, color=(100, 100, 100))
    params = {"value": 128, "invert": False}

    response = _post_threshold(client, image_bytes, params)

    assert response.status_code == 200
    body = response.json()
    assert body["width"] == 32
    assert body["height"] == 16
    assert body["effective_params"] == params
    assert body["content_type"] == "image/png"
    assert set(body["metrics"].keys()) == {"foreground_percent", "background_percent"}

    decoded_bytes = base64.b64decode(body["image_base64"])
    decoded_image = cv2.imdecode(np.frombuffer(decoded_bytes, dtype=np.uint8), cv2.IMREAD_GRAYSCALE)
    assert decoded_image.shape == (16, 32)
    # Máscara binaria de verdad: solo 0 o 255.
    assert set(np.unique(decoded_image).tolist()) <= {0, 255}


def test_threshold_is_deterministic_across_repeated_requests(client):
    image_bytes = make_png_bytes(10, 10, color=(77, 77, 77))
    params = {"value": 90, "invert": True}

    first = _post_threshold(client, image_bytes, params)
    second = _post_threshold(client, image_bytes, params)

    assert first.status_code == second.status_code == 200
    assert first.json()["image_base64"] == second.json()["image_base64"]
    assert first.json()["metrics"] == second.json()["metrics"]


def test_threshold_with_light_image_produces_near_full_mask(client):
    # Imagen clara (245,245,245 BGR): con el umbral por defecto queda casi
    # completamente blanca (foreground ~100%).
    image_bytes = make_png_bytes(8, 8, color=(245, 245, 245))

    response = _post_threshold(client, image_bytes, {"value": 128, "invert": False})

    assert response.status_code == 200
    assert response.json()["metrics"]["foreground_percent"] == pytest.approx(100.0)


def test_threshold_with_dark_image_produces_near_empty_mask(client):
    # Imagen oscura (8,8,8 BGR): con el umbral por defecto queda casi
    # completamente negra (foreground ~0%).
    image_bytes = make_png_bytes(8, 8, color=(8, 8, 8))

    response = _post_threshold(client, image_bytes, {"value": 128, "invert": False})

    assert response.status_code == 200
    assert response.json()["metrics"]["foreground_percent"] == pytest.approx(0.0)


def test_threshold_with_transparent_image_succeeds(client):
    image_bytes = make_rgba_png_bytes(8, 8, color=(180, 180, 180), alpha=32)

    response = _post_threshold(client, image_bytes, {"value": 128, "invert": False})

    assert response.status_code == 200
    body = response.json()
    assert body["width"] == 8
    assert body["height"] == 8


def test_threshold_treats_fully_transparent_region_as_background(client):
    # Mismo color RGB claro (200,200,200) en ambas mitades -- sin
    # transparencia, ambas caerían del lado foreground con el umbral por
    # defecto. Solo la mitad transparente (alpha=0) debe quedar como
    # background en la máscara resultante.
    image_bytes = make_half_transparent_rgba_png_bytes(8, 8, color=(200, 200, 200))

    response = _post_threshold(client, image_bytes, {"value": 128, "invert": False})

    assert response.status_code == 200
    decoded = cv2.imdecode(
        np.frombuffer(base64.b64decode(response.json()["image_base64"]), dtype=np.uint8),
        cv2.IMREAD_GRAYSCALE,
    )
    assert (decoded[:, :4] == 255).all()
    assert (decoded[:, 4:] == 0).all()


def test_threshold_produces_empty_mask_when_value_is_maximum(client):
    image_bytes = make_png_bytes(6, 6, color=(200, 200, 200))

    response = _post_threshold(client, image_bytes, {"value": 255, "invert": False})

    assert response.status_code == 200
    assert response.json()["metrics"]["foreground_percent"] == pytest.approx(0.0)


def test_threshold_produces_full_mask_when_value_is_maximum_and_inverted(client):
    image_bytes = make_png_bytes(6, 6, color=(200, 200, 200))

    response = _post_threshold(client, image_bytes, {"value": 255, "invert": True})

    assert response.status_code == 200
    assert response.json()["metrics"]["foreground_percent"] == pytest.approx(100.0)


def test_threshold_rejects_corrupt_file(client):
    response = _post_threshold(client, NOT_AN_IMAGE, {"value": 128, "invert": False})

    assert response.status_code == 400
    assert response.json()["code"] == "corrupt_image"


@pytest.mark.parametrize(
    "params",
    [
        {"value": -1, "invert": False},
        {"value": 256, "invert": False},
    ],
)
def test_threshold_rejects_out_of_range_value(client, params):
    image_bytes = make_png_bytes(4, 4)

    response = _post_threshold(client, image_bytes, params)

    assert response.status_code == 422
    assert response.json()["code"] == "invalid_parameters"


@pytest.mark.parametrize("value", [0, 255])
def test_threshold_accepts_boundary_values(client, value):
    image_bytes = make_png_bytes(4, 4)

    response = _post_threshold(client, image_bytes, {"value": value, "invert": False})

    assert response.status_code == 200
    assert response.json()["effective_params"]["value"] == value


def test_threshold_rejects_malformed_params_json(client):
    image_bytes = make_png_bytes(4, 4)

    response = client.post(
        THRESHOLD_URL,
        files={"file": ("preview.png", image_bytes, "image/png")},
        data={"params": "no es json"},
    )

    assert response.status_code == 422
    assert response.json()["code"] == "invalid_parameters"


def test_threshold_rejects_oversized_image():
    from fastapi.testclient import TestClient

    app.dependency_overrides[get_settings] = lambda: Settings(
        max_image_width=4, max_image_height=4, max_image_pixels=16
    )
    try:
        with TestClient(app) as tiny_client:
            image_bytes = make_png_bytes(50, 50)
            response = _post_threshold(tiny_client, image_bytes, {"value": 128, "invert": False})

            assert response.status_code == 413
            assert response.json()["code"] == "dimensions_exceeded"
    finally:
        app.dependency_overrides.pop(get_settings, None)
