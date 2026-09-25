"""Tests de integración de POST /api/v1/simplify vía TestClient: contrato de
respuesta, validación de parámetros y los casos de prueba obligatorios de
spec.md M1-S07 ("Pruebas"): SVG de entrada corrupto, tolerancias fuera de
rango y timeout.
"""

import json

import pytest

from app.api.dependencies import get_simplification_service
from app.core.config import Settings, get_settings
from app.main import app
from app.services.simplification_service import SimplificationService

SIMPLIFY_URL = "/api/v1/simplify"

SQUARE_SVG = (
    '<svg xmlns="http://www.w3.org/2000/svg" width="60" height="60">'
    '<path d="M10,10 L10,20 L10,30 L10,40 L10,50 L50,50 L50,10 Z" fill="#000000"/>'
    "</svg>"
).encode("utf-8")


def _post_simplify(client, svg_bytes: bytes, params: dict, filename: str = "vector.svg"):
    return client.post(
        SIMPLIFY_URL,
        files={"file": (filename, svg_bytes, "image/svg+xml")},
        data={"params": json.dumps(params)},
    )


def test_simplify_returns_expected_contract(client):
    response = _post_simplify(client, SQUARE_SVG, {"epsilon_ratio": 0.05})

    assert response.status_code == 200
    body = response.json()
    assert body["content_type"] == "image/svg+xml"
    assert body["effective_params"] == {"epsilon_ratio": 0.05}
    assert set(body["metrics"].keys()) == {"before", "after", "reduction_percent"}
    assert body["metrics"]["after"]["approx_node_count"] < body["metrics"]["before"]["approx_node_count"]
    assert body["metrics"]["reduction_percent"] > 0
    assert "<svg" in body["svg"]
    assert "<path" in body["svg"]


def test_simplify_is_deterministic_across_repeated_requests(client):
    first = _post_simplify(client, SQUARE_SVG, {"epsilon_ratio": 0.05})
    second = _post_simplify(client, SQUARE_SVG, {"epsilon_ratio": 0.05})

    assert first.status_code == second.status_code == 200
    assert first.json()["svg"] == second.json()["svg"]
    assert first.json()["metrics"] == second.json()["metrics"]


def test_simplify_result_has_no_script_tags(client):
    malicious_svg = (
        '<svg xmlns="http://www.w3.org/2000/svg" width="60" height="60">'
        "<script>alert(1)</script>"
        '<path d="M10,10 L10,20 L10,30 L50,50 L50,10 Z" onload="alert(2)"/>'
        "</svg>"
    ).encode("utf-8")

    response = _post_simplify(client, malicious_svg, {"epsilon_ratio": 0.02})

    assert response.status_code == 200
    assert "<script" not in response.json()["svg"]
    assert "onload" not in response.json()["svg"]


def test_simplify_rejects_corrupt_svg(client):
    response = _post_simplify(client, b"esto no es un SVG", {"epsilon_ratio": 0.05})

    assert response.status_code == 400
    assert response.json()["code"] == "invalid_input_svg"


@pytest.mark.parametrize(
    "params",
    [
        {"epsilon_ratio": 0},
        {"epsilon_ratio": -0.1},
        {"epsilon_ratio": 0.6},
    ],
)
def test_simplify_rejects_out_of_range_epsilon_ratio(client, params):
    response = _post_simplify(client, SQUARE_SVG, params)

    assert response.status_code == 422
    assert response.json()["code"] == "invalid_parameters"


def test_simplify_rejects_malformed_params_json(client):
    response = client.post(
        SIMPLIFY_URL,
        files={"file": ("vector.svg", SQUARE_SVG, "image/svg+xml")},
        data={"params": "no es json"},
    )

    assert response.status_code == 422
    assert response.json()["code"] == "invalid_parameters"


def test_simplify_rejects_oversized_input_svg():
    from fastapi.testclient import TestClient

    app.dependency_overrides[get_settings] = lambda: Settings(max_svg_output_bytes=10)
    try:
        with TestClient(app) as tiny_client:
            response = _post_simplify(tiny_client, SQUARE_SVG, {"epsilon_ratio": 0.05})

            assert response.status_code == 413
            assert response.json()["code"] == "svg_input_too_large"
    finally:
        app.dependency_overrides.pop(get_settings, None)


def test_simplify_reports_timeout_as_typed_error_not_unhandled_exception():
    from fastapi.testclient import TestClient

    def slow_simplify(svg_text: str, epsilon_ratio: float) -> str:
        import time

        time.sleep(0.5)
        return svg_text

    slow_settings = Settings(simplify_timeout_seconds=0)
    app.dependency_overrides[get_simplification_service] = lambda: SimplificationService(
        slow_settings, simplify_fn=slow_simplify
    )
    try:
        with TestClient(app) as slow_client:
            response = _post_simplify(slow_client, SQUARE_SVG, {"epsilon_ratio": 0.05})

            assert response.status_code == 504
            assert response.json()["code"] == "simplification_timeout"
    finally:
        app.dependency_overrides.pop(get_simplification_service, None)
