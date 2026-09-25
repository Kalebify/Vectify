"""Tests de integración de POST /api/v1/check vía TestClient: contrato de
respuesta, validación de parámetros y los casos de prueba obligatorios de
spec.md M1-S08 ("Pruebas"): SVG de entrada corrupto, tolerancias fuera de
rango y timeout.
"""

import json

import pytest

from app.api.dependencies import get_path_checker_service
from app.core.config import Settings, get_settings
from app.main import app
from app.services.path_checker_service import PathCheckerService

CHECK_URL = "/api/v1/check"

OPEN_AND_DUPLICATE_SVG = (
    '<svg xmlns="http://www.w3.org/2000/svg" width="60" height="60">'
    '<path d="M10,10 L50,10 L50,50 L10,50 L10.02,10.01"/>'
    '<path d="M55,55 L58,55 L58,58 L55,58 Z"/>'
    '<path d="M55,55 L58,55 L58,58 L55,58 Z"/>'
    "</svg>"
).encode("utf-8")


def _post_check(client, svg_bytes: bytes, params: dict, filename: str = "vector.svg"):
    return client.post(
        CHECK_URL,
        files={"file": (filename, svg_bytes, "image/svg+xml")},
        data={"params": json.dumps(params)},
    )


def test_check_returns_expected_contract(client):
    response = _post_check(client, OPEN_AND_DUPLICATE_SVG, {})

    assert response.status_code == 200
    body = response.json()
    assert body["effective_params"] == {"close_gap_ratio": 0.005, "duplicate_point_ratio": 0.002}
    assert body["summary"] == {"open_path_count": 1, "duplicate_group_count": 1}
    assert body["skipped_path_count"] == 0
    types = sorted(issue["type"] for issue in body["issues"])
    assert types == ["duplicate_path", "open_path"]


def test_check_is_deterministic_across_repeated_requests(client):
    first = _post_check(client, OPEN_AND_DUPLICATE_SVG, {})
    second = _post_check(client, OPEN_AND_DUPLICATE_SVG, {})

    assert first.status_code == second.status_code == 200
    assert first.json() == second.json()


def test_check_never_modifies_or_returns_the_input_svg(client):
    response = _post_check(client, OPEN_AND_DUPLICATE_SVG, {})

    assert response.status_code == 200
    assert "svg" not in response.json()


def test_check_rejects_corrupt_svg(client):
    response = _post_check(client, b"esto no es un SVG", {})

    assert response.status_code == 400
    assert response.json()["code"] == "invalid_input_svg"


@pytest.mark.parametrize(
    "params",
    [
        {"close_gap_ratio": 0},
        {"close_gap_ratio": -0.1},
        {"close_gap_ratio": 0.6},
        {"duplicate_point_ratio": 0},
        {"duplicate_point_ratio": 0.6},
    ],
)
def test_check_rejects_out_of_range_tolerances(client, params):
    response = _post_check(client, OPEN_AND_DUPLICATE_SVG, params)

    assert response.status_code == 422
    assert response.json()["code"] == "invalid_parameters"


def test_check_rejects_malformed_params_json(client):
    response = client.post(
        CHECK_URL,
        files={"file": ("vector.svg", OPEN_AND_DUPLICATE_SVG, "image/svg+xml")},
        data={"params": "no es json"},
    )

    assert response.status_code == 422
    assert response.json()["code"] == "invalid_parameters"


def test_check_rejects_oversized_input_svg():
    from fastapi.testclient import TestClient

    app.dependency_overrides[get_settings] = lambda: Settings(max_svg_output_bytes=10)
    try:
        with TestClient(app) as tiny_client:
            response = _post_check(tiny_client, OPEN_AND_DUPLICATE_SVG, {})

            assert response.status_code == 413
            assert response.json()["code"] == "svg_input_too_large"
    finally:
        app.dependency_overrides.pop(get_settings, None)


def test_check_reports_timeout_as_typed_error_not_unhandled_exception():
    from fastapi.testclient import TestClient

    def slow_check(svg_text: str, close_gap_ratio: float, duplicate_point_ratio: float, max_subpaths: int) -> dict:
        import time

        time.sleep(0.5)
        return {"open_path_issues": [], "duplicate_issues": [], "skipped_path_count": 0}

    slow_settings = Settings(check_timeout_seconds=0)
    app.dependency_overrides[get_path_checker_service] = lambda: PathCheckerService(
        slow_settings, check_fn=slow_check
    )
    try:
        with TestClient(app) as slow_client:
            response = _post_check(slow_client, OPEN_AND_DUPLICATE_SVG, {})

            assert response.status_code == 504
            assert response.json()["code"] == "check_timeout"
    finally:
        app.dependency_overrides.pop(get_path_checker_service, None)


def test_check_reports_too_many_subpaths_as_typed_error():
    from fastapi.testclient import TestClient

    tiny_settings = Settings(max_check_subpaths=1)
    app.dependency_overrides[get_path_checker_service] = lambda: PathCheckerService(tiny_settings)
    try:
        with TestClient(app) as tiny_client:
            response = _post_check(tiny_client, OPEN_AND_DUPLICATE_SVG, {})

            assert response.status_code == 413
            assert response.json()["code"] == "too_many_subpaths"
    finally:
        app.dependency_overrides.pop(get_path_checker_service, None)
