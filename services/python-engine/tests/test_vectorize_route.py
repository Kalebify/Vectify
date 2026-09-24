"""Tests de integración de POST /api/v1/vectorize vía TestClient: contrato de
respuesta, casos de prueba obligatorios de spec.md M1-S05 ("Pruebas"): logos/
siluetas simples, agujeros internos, bordes/bounds, máscara vacía, timeout y
SVG resultante válido. Usa el motor VTracer real (sin mocks) salvo en el test
de timeout, donde se inyecta un motor lento vía dependency override (mismo
patrón que test_threshold_route.py usa TestClient directo, pero acá se
necesita reemplazar el motor, no solo los Settings).
"""

import xml.etree.ElementTree as ET

import pytest

from app.api.dependencies import get_vectorization_service
from app.core.config import Settings, get_settings
from app.core.vector_engine import VtracerEngine
from app.main import app
from app.services.vectorization_service import VectorizationService
from tests.support import NOT_AN_IMAGE, make_mask_png_bytes, make_ring_mask_png_bytes, make_square_mask_png_bytes

VECTORIZE_URL = "/api/v1/vectorize"


def _post_vectorize(client, mask_bytes: bytes, filename: str = "mask.png"):
    return client.post(VECTORIZE_URL, files={"file": (filename, mask_bytes, "image/png")})


def test_vectorize_simple_square_returns_expected_contract(client):
    mask_bytes = make_square_mask_png_bytes(size=60, square=30)

    response = _post_vectorize(client, mask_bytes)

    assert response.status_code == 200
    body = response.json()
    assert body["width"] == 60
    assert body["height"] == 60
    assert body["content_type"] == "image/svg+xml"
    assert body["metrics"]["path_count"] == 1
    assert set(body["metrics"].keys()) == {"path_count", "approx_node_count", "bounds"}
    assert set(body["metrics"]["bounds"].keys()) == {"min_x", "min_y", "max_x", "max_y", "width", "height"}


def test_vectorize_result_is_valid_xml(client):
    mask_bytes = make_square_mask_png_bytes()

    response = _post_vectorize(client, mask_bytes)

    svg = response.json()["svg"]
    root = ET.fromstring(svg)  # no lanza -> XML bien formado
    assert root.tag.endswith("svg")


def test_vectorize_result_has_no_script_tags(client):
    mask_bytes = make_square_mask_png_bytes()

    response = _post_vectorize(client, mask_bytes)

    assert "<script" not in response.json()["svg"]


def test_vectorize_is_deterministic_across_repeated_requests(client):
    mask_bytes = make_square_mask_png_bytes(size=40, square=16)

    first = _post_vectorize(client, mask_bytes)
    second = _post_vectorize(client, mask_bytes)

    assert first.status_code == second.status_code == 200
    assert first.json()["svg"] == second.json()["svg"]
    assert first.json()["metrics"] == second.json()["metrics"]


def test_vectorize_bounds_reflect_a_shape_not_touching_the_canvas_edges(client):
    # Cuadrado de 30x30 centrado en un lienzo de 100x100 (offset 35): los
    # bounds deben ser mucho más chicos que el lienzo, no el lienzo entero.
    mask_bytes = make_square_mask_png_bytes(size=100, square=30)

    response = _post_vectorize(client, mask_bytes)

    bounds = response.json()["metrics"]["bounds"]
    assert bounds["width"] == pytest.approx(30.0, abs=1.0)
    assert bounds["height"] == pytest.approx(30.0, abs=1.0)
    assert bounds["min_x"] > 0
    assert bounds["max_x"] < 100


def test_vectorize_ring_with_hole_produces_single_path(client):
    mask_bytes = make_ring_mask_png_bytes()

    response = _post_vectorize(client, mask_bytes)

    assert response.status_code == 200
    assert response.json()["metrics"]["path_count"] == 1


def test_vectorize_rejects_corrupt_file(client):
    response = _post_vectorize(client, NOT_AN_IMAGE)

    assert response.status_code == 400
    assert response.json()["code"] == "corrupt_image"


def test_vectorize_rejects_empty_mask(client):
    mask_bytes = make_mask_png_bytes(20, 20)  # todo negro, sin foreground

    response = _post_vectorize(client, mask_bytes)

    assert response.status_code == 422
    assert response.json()["code"] == "empty_mask"


def test_vectorize_rejects_oversized_image():
    from fastapi.testclient import TestClient

    app.dependency_overrides[get_settings] = lambda: Settings(
        max_image_width=4, max_image_height=4, max_image_pixels=16
    )
    try:
        with TestClient(app) as tiny_client:
            mask_bytes = make_square_mask_png_bytes(size=50, square=20)
            response = _post_vectorize(tiny_client, mask_bytes)

            assert response.status_code == 413
            assert response.json()["code"] == "dimensions_exceeded"
    finally:
        app.dependency_overrides.pop(get_settings, None)


def test_vectorize_reports_timeout_as_typed_error_not_unhandled_exception():
    from fastapi.testclient import TestClient

    class SlowEngine:
        def trace(self, mask):
            import time

            time.sleep(0.5)
            return '<svg xmlns="http://www.w3.org/2000/svg" width="1" height="1"></svg>'

    slow_settings = Settings(vectorize_timeout_seconds=0)
    app.dependency_overrides[get_vectorization_service] = lambda: VectorizationService(
        slow_settings, engine=SlowEngine()
    )
    try:
        with TestClient(app) as slow_client:
            mask_bytes = make_square_mask_png_bytes()
            response = _post_vectorize(slow_client, mask_bytes)

            assert response.status_code == 504
            assert response.json()["code"] == "vectorization_timeout"
    finally:
        app.dependency_overrides.pop(get_vectorization_service, None)


def test_vectorization_service_default_engine_is_vtracer():
    # Confirma el wiring real (no un fake) para al menos un caso de punta a
    # punta -- el resto de los tests de contrato usan el `client` fixture,
    # que ya construye el servicio con VtracerEngine por defecto (ver
    # app.api.dependencies.get_vectorization_service).
    service = VectorizationService(Settings())
    assert isinstance(service._engine, VtracerEngine)  # noqa: SLF001 - test interno, acceso deliberado
