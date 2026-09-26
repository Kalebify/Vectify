"""Tests de integración de POST /api/v1/vectorize-layers (M2-S02) vía
TestClient: una capa vectorial POR CADA máscara de color recibida, cada una
vectorizada de forma INDEPENDIENTE reutilizando VectorizationService.process
(mismo motor/pipeline que M1-S05, sin reinventar el trazado de contornos), en
una única request .NET -> Python (ver spec.md, "Ambigüedades detectadas").
Casos de prueba obligatorios de spec.md M2-S02 ("Pruebas"): capas solapadas,
no solapadas, agujeros, transparencia (origen en zonas con alpha parcial) y
alineación pixel->vector con una tolerancia documentada.
"""

import json

import pytest

from tests.support import (
    NOT_AN_IMAGE,
    make_mask_png_bytes,
    make_positioned_square_mask_png_bytes,
    make_ring_mask_png_bytes,
    make_sparse_mask_png_bytes,
    make_square_mask_png_bytes,
)

VECTORIZE_LAYERS_URL = "/api/v1/vectorize-layers"

# Tolerancia de alineación pixel->vector: 1px absoluto sobre el bounding box
# del path resultante -- ver spec.md M2-S02, "Ambigüedades detectadas": "el
# implementador elige un valor razonable (ej. sub-píxel o 1px) y lo
# documenta, consistente con las tolerancias ya usadas en M1-S08". VTracer
# con mode="polygon" traza el contorno exacto de los píxeles blancos sin
# antialiasing/suavizado, así que 1px absoluto ya es una tolerancia holgada
# (el error esperado en el caso normal es 0) frente a las tolerancias
# RELATIVAS (fracción de diagonal) de CheckParams en M1-S08 -- acá se usa un
# valor absoluto en vez de relativo porque la comparación es contra
# coordenadas de píxel conocidas de antemano, no contra otro subpath.
ALIGNMENT_TOLERANCE_PX = 1.0


def _post_vectorize_layers(client, masks: list[bytes], group_ids: list[str]):
    files = [("files", (f"mask-{index}.png", data, "image/png")) for index, data in enumerate(masks)]
    return client.post(VECTORIZE_LAYERS_URL, files=files, data={"group_ids": json.dumps(group_ids)})


def test_vectorize_layers_returns_one_layer_per_mask_matching_group_ids(client):
    masks = [make_square_mask_png_bytes(size=60, square=30), make_ring_mask_png_bytes()]
    group_ids = ["group-a", "group-b"]

    response = _post_vectorize_layers(client, masks, group_ids)

    assert response.status_code == 200
    body = response.json()
    assert [layer["group_id"] for layer in body["layers"]] == group_ids
    for layer in body["layers"]:
        assert layer["content_type"] == "image/svg+xml"
        assert layer["metrics"]["path_count"] >= 1


def test_vectorize_layers_share_the_same_uncropped_canvas_dimensions_across_layers(client):
    # Ninguna capa se recorta a su propio bounding box: todas comparten el
    # mismo lienzo/viewBox que la imagen original -- ver spec.md,
    # "normalización de coordenadas". Dos cuadrados chicos en esquinas
    # opuestas de un lienzo de 100x100 deben reportar width=height=100 los
    # dos, no el tamaño de su propia forma.
    size = 100
    masks = [
        make_positioned_square_mask_png_bytes(size=size, square=10, offset_x=5, offset_y=5),
        make_positioned_square_mask_png_bytes(size=size, square=10, offset_x=80, offset_y=80),
    ]

    response = _post_vectorize_layers(client, masks, ["top-left", "bottom-right"])

    assert response.status_code == 200
    for layer in response.json()["layers"]:
        assert layer["width"] == size
        assert layer["height"] == size


def test_vectorize_layers_non_overlapping_masks_each_keep_their_own_position(client):
    size = 100
    masks = [
        make_positioned_square_mask_png_bytes(size=size, square=20, offset_x=0, offset_y=0),
        make_positioned_square_mask_png_bytes(size=size, square=20, offset_x=70, offset_y=70),
    ]

    response = _post_vectorize_layers(client, masks, ["top-left", "bottom-right"])

    assert response.status_code == 200
    layers = response.json()["layers"]
    top_left_bounds = layers[0]["metrics"]["bounds"]
    bottom_right_bounds = layers[1]["metrics"]["bounds"]

    assert top_left_bounds["max_x"] < bottom_right_bounds["min_x"]
    assert top_left_bounds["max_y"] < bottom_right_bounds["min_y"]


def test_vectorize_layers_overlapping_masks_are_each_vectorized_independently(client):
    # Dos cuadrados de 40x40 cuyas posiciones se superponen en el espacio
    # original (franja x=[30,50) e y=[30,50) en común): cada máscara es un
    # archivo separado, así que cada una se vectoriza de forma
    # completamente independiente -- ninguna capa "sabe" de la otra ni
    # queda recortada a la intersección/unión.
    size = 100
    masks = [
        make_positioned_square_mask_png_bytes(size=size, square=40, offset_x=10, offset_y=10),
        make_positioned_square_mask_png_bytes(size=size, square=40, offset_x=30, offset_y=30),
    ]

    response = _post_vectorize_layers(client, masks, ["a", "b"])

    assert response.status_code == 200
    for layer in response.json()["layers"]:
        bounds = layer["metrics"]["bounds"]
        assert bounds["width"] == pytest.approx(40.0, abs=1.0)
        assert bounds["height"] == pytest.approx(40.0, abs=1.0)


def test_vectorize_layers_hole_topology_matches_a_single_direct_vectorization(client):
    masks = [make_square_mask_png_bytes(size=60, square=30), make_ring_mask_png_bytes()]

    response = _post_vectorize_layers(client, masks, ["solid", "ring"])

    ring_layer = response.json()["layers"][1]
    # Mismo criterio que test_vectorize_ring_with_hole_produces_single_path
    # (M1-S05, test_vectorize_route.py): un único <path> con subpaths
    # anidados de sentido opuesto (fill-rule por winding), no dos paths
    # separados -- reutiliza el mismo motor/pipeline, así que la topología
    # de agujeros se preserva igual dentro del batch.
    assert ring_layer["metrics"]["path_count"] == 1


def test_vectorize_layers_mask_from_partial_alpha_group_succeeds(client):
    masks = [make_sparse_mask_png_bytes(size=40, block=4, gap=4)]

    response = _post_vectorize_layers(client, masks, ["semi-transparent-group"])

    assert response.status_code == 200
    layer = response.json()["layers"][0]
    assert layer["metrics"]["path_count"] > 0


def test_vectorize_layers_pixel_to_vector_alignment_within_documented_tolerance(client):
    size = 100
    square = 30
    offset = 25  # (size - square) // 2, mismo criterio que make_square_mask_png_bytes
    masks = [make_positioned_square_mask_png_bytes(size=size, square=square, offset_x=offset, offset_y=offset)]

    response = _post_vectorize_layers(client, masks, ["only"])

    bounds = response.json()["layers"][0]["metrics"]["bounds"]

    # Punto conocido DENTRO de la máscara original (centro del cuadrado)
    # debe caer dentro del bounding box del path resultante, en el MISMO
    # sistema de coordenadas de la imagen original (sin normalización propia
    # por capa) -- tolerancia documentada: ALIGNMENT_TOLERANCE_PX.
    foreground_x, foreground_y = offset + square / 2, offset + square / 2
    assert bounds["min_x"] - ALIGNMENT_TOLERANCE_PX <= foreground_x <= bounds["max_x"] + ALIGNMENT_TOLERANCE_PX
    assert bounds["min_y"] - ALIGNMENT_TOLERANCE_PX <= foreground_y <= bounds["max_y"] + ALIGNMENT_TOLERANCE_PX

    # Punto conocido FUERA de la máscara (esquina opuesta del lienzo, lejos
    # del cuadrado) debe caer claramente fuera del bounding box, con el
    # mismo margen de tolerancia.
    background_x, background_y = 2, 2
    assert not (
        bounds["min_x"] - ALIGNMENT_TOLERANCE_PX <= background_x <= bounds["max_x"] + ALIGNMENT_TOLERANCE_PX
        and bounds["min_y"] - ALIGNMENT_TOLERANCE_PX <= background_y <= bounds["max_y"] + ALIGNMENT_TOLERANCE_PX
    )


def test_vectorize_layers_is_deterministic_across_repeated_requests(client):
    masks = [make_square_mask_png_bytes(size=40, square=16)]

    first = _post_vectorize_layers(client, masks, ["x"])
    second = _post_vectorize_layers(client, masks, ["x"])

    assert first.status_code == second.status_code == 200
    assert first.json() == second.json()


def test_vectorize_layers_rejects_when_group_ids_count_does_not_match_files(client):
    masks = [make_square_mask_png_bytes(), make_square_mask_png_bytes()]

    response = _post_vectorize_layers(client, masks, ["only-one"])

    assert response.status_code == 422
    assert response.json()["code"] == "invalid_parameters"


def test_vectorize_layers_rejects_malformed_group_ids_json(client):
    files = [("files", ("mask.png", make_square_mask_png_bytes(), "image/png"))]

    response = client.post(VECTORIZE_LAYERS_URL, files=files, data={"group_ids": "no es json"})

    assert response.status_code == 422
    assert response.json()["code"] == "invalid_parameters"


def test_vectorize_layers_rejects_when_any_mask_is_corrupt(client):
    masks = [make_square_mask_png_bytes(), NOT_AN_IMAGE]

    response = _post_vectorize_layers(client, masks, ["good", "bad"])

    assert response.status_code == 400
    assert response.json()["code"] == "corrupt_image"


def test_vectorize_layers_rejects_when_any_mask_is_empty(client):
    masks = [make_square_mask_png_bytes(), make_mask_png_bytes(20, 20)]

    response = _post_vectorize_layers(client, masks, ["good", "empty"])

    assert response.status_code == 422
    assert response.json()["code"] == "empty_mask"
