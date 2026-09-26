"""Tests de integración de POST /api/v1/components/union vía TestClient:
contrato de respuesta, validación de parámetros y los casos de prueba
obligatorios de spec.md M2-S06 ("Pruebas"): piezas separadas (bridge),
geometría inválida, selección incoherente, timeout y SVG de entrada
demasiado grande.
"""

import json

from app.api.dependencies import get_physical_union_service
from app.core.config import Settings, get_settings
from app.main import app
from app.services.physical_union_service import PhysicalUnionService

UNION_URL = "/api/v1/components/union"

TWO_SEPARATED_SQUARES_SVG = (
    '<svg xmlns="http://www.w3.org/2000/svg" width="100" height="100">'
    '<path d="M0,0 L20,0 L20,20 L0,20 Z" fill="#000000"/>'
    '<path d="M70,70 L90,70 L90,90 L70,90 Z" fill="#000000"/>'
    "</svg>"
).encode("utf-8")

VALID_SELECTIONS = [
    {"component_id": "component-1", "members": [{"path_index": 0, "subpath_index": 0, "role": "solid"}]},
    {"component_id": "component-2", "members": [{"path_index": 1, "subpath_index": 0, "role": "solid"}]},
]

SELF_INTERSECTING_BOWTIE_SVG = (
    '<svg xmlns="http://www.w3.org/2000/svg" width="100" height="100">'
    '<path d="M0,0 L40,40 L40,0 L0,40 Z" fill="#000000"/>'
    '<path d="M60,60 L100,60 L100,100 L60,100 Z" fill="#000000"/>'
    "</svg>"
).encode("utf-8")


def _post_union(client, svg_bytes: bytes, params: dict, filename: str = "layer.svg"):
    return client.post(
        UNION_URL,
        files={"file": (filename, svg_bytes, "image/svg+xml")},
        data={"params": json.dumps(params)},
    )


def test_union_returns_expected_contract_for_separated_squares(client):
    response = _post_union(client, TWO_SEPARATED_SQUARES_SVG, {"selections": VALID_SELECTIONS})

    assert response.status_code == 200
    body = response.json()
    assert body["component_count_before"] == 2
    assert body["component_count_after"] == 1
    assert body["strategy"] == "bridge"
    assert body["bridge_count"] == 1
    assert body["width"] == 100
    assert body["height"] == 100
    assert "<path" in body["svg"]


def test_union_is_deterministic_across_repeated_requests(client):
    first = _post_union(client, TWO_SEPARATED_SQUARES_SVG, {"selections": VALID_SELECTIONS})
    second = _post_union(client, TWO_SEPARATED_SQUARES_SVG, {"selections": VALID_SELECTIONS})

    assert first.status_code == second.status_code == 200
    assert first.json() == second.json()


def test_union_rejects_corrupt_svg(client):
    response = _post_union(client, b"esto no es un SVG", {"selections": VALID_SELECTIONS})

    assert response.status_code == 400
    assert response.json()["code"] == "invalid_input_svg"


def test_union_rejects_selection_with_fewer_than_two_components(client):
    response = _post_union(client, TWO_SEPARATED_SQUARES_SVG, {"selections": [VALID_SELECTIONS[0]]})

    assert response.status_code == 422
    assert response.json()["code"] == "invalid_parameters"


def test_union_rejects_malformed_params_json(client):
    response = client.post(
        UNION_URL,
        files={"file": ("layer.svg", TWO_SEPARATED_SQUARES_SVG, "image/svg+xml")},
        data={"params": "no es json"},
    )

    assert response.status_code == 422
    assert response.json()["code"] == "invalid_parameters"


def test_union_rejects_self_intersecting_component_with_a_clear_error(client):
    response = _post_union(client, SELF_INTERSECTING_BOWTIE_SVG, {"selections": VALID_SELECTIONS})

    assert response.status_code == 422
    body = response.json()
    assert body["code"] == "physical_union_invalid_geometry"
    assert "component-1" in body["message"]


def test_union_rejects_oversized_input_svg():
    from fastapi.testclient import TestClient

    app.dependency_overrides[get_settings] = lambda: Settings(max_svg_output_bytes=10)
    try:
        with TestClient(app) as tiny_client:
            response = _post_union(tiny_client, TWO_SEPARATED_SQUARES_SVG, {"selections": VALID_SELECTIONS})

            assert response.status_code == 413
            assert response.json()["code"] == "svg_input_too_large"
    finally:
        app.dependency_overrides.pop(get_settings, None)


def test_union_reports_timeout_as_typed_error_not_unhandled_exception():
    from fastapi.testclient import TestClient

    def slow_union(svg_text, selections, touch_ratio, tiny_area_ratio, bridge_width_ratio, max_subpaths) -> dict:
        import time

        time.sleep(0.5)
        return {
            "svg": svg_text,
            "component_count_before": 2,
            "component_count_after": 1,
            "expected_component_count_after": 1,
            "strategy": "bridge",
            "bridge_count": 1,
        }

    slow_settings = Settings(physical_union_timeout_seconds=0)
    app.dependency_overrides[get_physical_union_service] = lambda: PhysicalUnionService(
        slow_settings, union_fn=slow_union
    )
    try:
        with TestClient(app) as slow_client:
            response = _post_union(slow_client, TWO_SEPARATED_SQUARES_SVG, {"selections": VALID_SELECTIONS})

            assert response.status_code == 504
            assert response.json()["code"] == "physical_union_timeout"
    finally:
        app.dependency_overrides.pop(get_physical_union_service, None)


def test_union_reports_impossible_union_as_typed_422_error():
    from fastapi.testclient import TestClient

    from app.core.errors import PhysicalUnionImpossibleError

    def failing_union(svg_text, selections, touch_ratio, tiny_area_ratio, bridge_width_ratio, max_subpaths) -> dict:
        raise PhysicalUnionImpossibleError(
            "Tras la unión, el análisis de componentes detectó 2 pieza(s) en vez de la 1 esperada."
        )

    app.dependency_overrides[get_physical_union_service] = lambda: PhysicalUnionService(
        Settings(), union_fn=failing_union
    )
    try:
        with TestClient(app) as impossible_client:
            response = _post_union(impossible_client, TWO_SEPARATED_SQUARES_SVG, {"selections": VALID_SELECTIONS})

            assert response.status_code == 422
            assert response.json()["code"] == "physical_union_impossible"
    finally:
        app.dependency_overrides.pop(get_physical_union_service, None)
