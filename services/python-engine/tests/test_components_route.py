"""Tests de integración de POST /api/v1/components vía TestClient: contrato
de respuesta, validación de parámetros y los casos de prueba obligatorios de
spec.md M2-S03 ("Pruebas"): SVG de entrada corrupto, tolerancias fuera de
rango y timeout/demasiados subpaths.
"""

import json

import pytest

from app.api.dependencies import get_component_analysis_service
from app.core.config import Settings, get_settings
from app.main import app
from app.services.component_analysis_service import ComponentAnalysisService

COMPONENTS_URL = "/api/v1/components"

TWO_ISLANDS_AND_A_TOUCHING_PAIR_SVG = (
    '<svg xmlns="http://www.w3.org/2000/svg" width="120" height="40">'
    '<path d="M0,0 L20,0 L20,20 L0,20 Z"/>'
    '<path d="M60,0 L100,0 L100,40 L60,40 Z"/>'
    '<path d="M100,0 L120,0 L120,40 L100,40 Z"/>'
    "</svg>"
).encode("utf-8")


def _post_components(client, svg_bytes: bytes, params: dict, filename: str = "layer.svg"):
    return client.post(
        COMPONENTS_URL,
        files={"file": (filename, svg_bytes, "image/svg+xml")},
        data={"params": json.dumps(params)},
    )


def test_components_returns_expected_contract(client):
    response = _post_components(client, TWO_ISLANDS_AND_A_TOUCHING_PAIR_SVG, {})

    assert response.status_code == 200
    body = response.json()
    assert body["effective_params"] == {"touch_ratio": 0.001, "tiny_area_ratio": 0.0005}
    assert body["summary"]["component_count"] == 2
    assert body["skipped_path_count"] == 0
    member_counts = sorted(len(c["members"]) for c in body["components"])
    assert member_counts == [1, 2]


def test_components_is_deterministic_across_repeated_requests(client):
    first = _post_components(client, TWO_ISLANDS_AND_A_TOUCHING_PAIR_SVG, {})
    second = _post_components(client, TWO_ISLANDS_AND_A_TOUCHING_PAIR_SVG, {})

    assert first.status_code == second.status_code == 200
    assert first.json() == second.json()


def test_components_never_modifies_or_returns_the_input_svg(client):
    response = _post_components(client, TWO_ISLANDS_AND_A_TOUCHING_PAIR_SVG, {})

    assert response.status_code == 200
    assert "svg" not in response.json()


def test_components_rejects_corrupt_svg(client):
    response = _post_components(client, b"esto no es un SVG", {})

    assert response.status_code == 400
    assert response.json()["code"] == "invalid_input_svg"


@pytest.mark.parametrize(
    "params",
    [
        {"touch_ratio": -0.1},
        {"touch_ratio": 0.6},
        {"tiny_area_ratio": -0.1},
        {"tiny_area_ratio": 0.6},
    ],
)
def test_components_rejects_out_of_range_tolerances(client, params):
    response = _post_components(client, TWO_ISLANDS_AND_A_TOUCHING_PAIR_SVG, params)

    assert response.status_code == 422
    assert response.json()["code"] == "invalid_parameters"


def test_components_rejects_malformed_params_json(client):
    response = client.post(
        COMPONENTS_URL,
        files={"file": ("layer.svg", TWO_ISLANDS_AND_A_TOUCHING_PAIR_SVG, "image/svg+xml")},
        data={"params": "no es json"},
    )

    assert response.status_code == 422
    assert response.json()["code"] == "invalid_parameters"


def test_components_rejects_oversized_input_svg():
    from fastapi.testclient import TestClient

    app.dependency_overrides[get_settings] = lambda: Settings(max_svg_output_bytes=10)
    try:
        with TestClient(app) as tiny_client:
            response = _post_components(tiny_client, TWO_ISLANDS_AND_A_TOUCHING_PAIR_SVG, {})

            assert response.status_code == 413
            assert response.json()["code"] == "svg_input_too_large"
    finally:
        app.dependency_overrides.pop(get_settings, None)


def test_components_reports_timeout_as_typed_error_not_unhandled_exception():
    from fastapi.testclient import TestClient

    def slow_analyze(svg_text: str, touch_ratio: float, tiny_area_ratio: float, max_subpaths: int) -> dict:
        import time

        time.sleep(0.5)
        return {"components": [], "skipped_path_count": 0}

    slow_settings = Settings(component_timeout_seconds=0)
    app.dependency_overrides[get_component_analysis_service] = lambda: ComponentAnalysisService(
        slow_settings, analyze_fn=slow_analyze
    )
    try:
        with TestClient(app) as slow_client:
            response = _post_components(slow_client, TWO_ISLANDS_AND_A_TOUCHING_PAIR_SVG, {})

            assert response.status_code == 504
            assert response.json()["code"] == "component_analysis_timeout"
    finally:
        app.dependency_overrides.pop(get_component_analysis_service, None)


def test_components_reports_too_many_subpaths_as_typed_error():
    from fastapi.testclient import TestClient

    tiny_settings = Settings(max_component_subpaths=1)
    app.dependency_overrides[get_component_analysis_service] = lambda: ComponentAnalysisService(tiny_settings)
    try:
        with TestClient(app) as tiny_client:
            response = _post_components(tiny_client, TWO_ISLANDS_AND_A_TOUCHING_PAIR_SVG, {})

            assert response.status_code == 413
            assert response.json()["code"] == "too_many_component_subpaths"
    finally:
        app.dependency_overrides.pop(get_component_analysis_service, None)
