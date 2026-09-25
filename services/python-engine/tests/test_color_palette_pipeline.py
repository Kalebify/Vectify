"""Tests unitarios de las funciones puras de detección/reducción de paleta
de colores (app.core.color_palette_pipeline): agrupamiento determinista en
espacio Lab, manejo explícito de transparencia y respeto del límite
`max_colors`. Ver spec.md M2-S01, sección "Pruebas": colores sólidos,
anti-aliasing, sombras, transparencias, colores casi iguales y muchos
colores.
"""

import numpy as np
import pytest

from app.core.color_palette_pipeline import build_quantized_preview, detect_palette, extract_unique_colors


def _solid_block_image() -> np.ndarray:
    """6x9 BGR: tres bloques sólidos bien separados (rojo, verde, azul puros)."""
    image = np.zeros((6, 9, 3), dtype=np.uint8)
    image[:, 0:3] = (0, 0, 255)  # BGR: rojo puro
    image[:, 3:6] = (0, 255, 0)  # verde puro
    image[:, 6:9] = (255, 0, 0)  # azul puro
    return image


# --- Colores sólidos ---


def test_detect_palette_solid_colors_returns_three_separate_groups_with_expected_area():
    image = _solid_block_image()

    result = detect_palette(image, alpha=None, tolerance=5.0, max_colors=None, max_unique_colors=512)

    assert len(result.groups) == 3
    for group in result.groups:
        assert group.pixel_count == 18  # 6 filas * 3 columnas
        assert not group.has_partial_alpha
    assert sum(g.pixel_count for g in result.groups) == result.total_pixel_count


def test_detect_palette_is_deterministic_across_repeated_calls():
    image = _solid_block_image()

    first = detect_palette(image, None, 5.0, None, 512)
    second = detect_palette(image, None, 5.0, None, 512)

    assert [g.color_bgr for g in first.groups] == [g.color_bgr for g in second.groups]
    assert [g.pixel_count for g in first.groups] == [g.pixel_count for g in second.groups]
    for g1, g2 in zip(first.groups, second.groups):
        assert np.array_equal(g1.mask, g2.mask)


def test_detect_palette_groups_are_ordered_by_area_descending():
    image = np.zeros((10, 10, 3), dtype=np.uint8)
    image[:, :7] = (10, 10, 10)
    image[:, 7:] = (250, 250, 250)

    result = detect_palette(image, None, 5.0, None, 512)

    assert result.groups[0].pixel_count >= result.groups[1].pixel_count


# --- Sombras (variaciones de luminosidad del mismo color lógico) ---


def _shadow_image() -> np.ndarray:
    base = np.array([180, 90, 40], dtype=np.uint8)  # BGR
    shadow = (base.astype(np.float64) * 0.55).astype(np.uint8)
    image = np.zeros((4, 4, 3), dtype=np.uint8)
    image[:, :2] = base
    image[:, 2:] = shadow
    return image


def test_detect_palette_shadow_variations_merge_with_wide_tolerance():
    result = detect_palette(_shadow_image(), None, tolerance=60.0, max_colors=None, max_unique_colors=512)

    assert len(result.groups) == 1


def test_detect_palette_shadow_variations_stay_separate_with_strict_tolerance():
    result = detect_palette(_shadow_image(), None, tolerance=1.0, max_colors=None, max_unique_colors=512)

    assert len(result.groups) == 2


# --- Colores casi iguales ---


def _near_identical_colors_image() -> np.ndarray:
    image = np.zeros((4, 4, 3), dtype=np.uint8)
    image[:, :2] = (100, 150, 200)
    image[:, 2:] = (103, 152, 202)
    return image


def test_detect_palette_near_identical_colors_merge_within_tolerance():
    result = detect_palette(_near_identical_colors_image(), None, tolerance=10.0, max_colors=None, max_unique_colors=512)

    assert len(result.groups) == 1


def test_detect_palette_near_identical_colors_stay_separate_with_zero_tolerance():
    result = detect_palette(_near_identical_colors_image(), None, tolerance=0.0, max_colors=None, max_unique_colors=512)

    assert len(result.groups) == 2


# --- Anti-aliasing (bordes con gradiente de color) ---


def _antialiased_edge_image(width: int = 20, height: int = 4) -> np.ndarray:
    left = np.array([30, 30, 200], dtype=np.float64)
    right = np.array([200, 30, 30], dtype=np.float64)
    image = np.zeros((height, width, 3), dtype=np.uint8)
    edge_start, edge_width = 8, 4
    for x in range(width):
        if x < edge_start:
            color = left
        elif x >= edge_start + edge_width:
            color = right
        else:
            t = (x - edge_start) / (edge_width - 1)
            color = left * (1 - t) + right * t
        image[:, x] = color.astype(np.uint8)
    return image


def test_detect_palette_antialiased_edge_still_covers_every_pixel_and_respects_max_colors():
    image = _antialiased_edge_image()

    result = detect_palette(image, None, tolerance=80.0, max_colors=3, max_unique_colors=512)

    assert len(result.groups) <= 3
    assert sum(g.pixel_count for g in result.groups) == image.shape[0] * image.shape[1]


def test_detect_palette_antialiased_edge_produces_more_than_two_raw_colors_without_a_limit():
    # Sin `max_colors`, el degradé intermedio no debería colapsar por completo
    # a solo los dos colores de los extremos -- hay tonos de transición
    # realmente distintos entre sí más allá de la tolerancia por defecto.
    image = _antialiased_edge_image()

    result = detect_palette(image, None, tolerance=5.0, max_colors=None, max_unique_colors=512)

    assert len(result.groups) > 2


# --- Transparencia ---


def test_detect_palette_fully_transparent_image_produces_empty_palette():
    image = np.full((4, 4, 3), (10, 20, 30), dtype=np.uint8)
    alpha = np.zeros((4, 4), dtype=np.uint8)

    result = detect_palette(image, alpha, tolerance=5.0, max_colors=None, max_unique_colors=512)

    assert result.groups == []
    assert result.transparent_pixel_count == 16


def test_detect_palette_excludes_only_fully_transparent_pixels_from_color_groups():
    # Mismo color RGB en toda la imagen; solo la mitad derecha es
    # transparente (alpha=0) -- no debe contarse como color, a diferencia de
    # un fondo sólido real del mismo color.
    image = np.full((4, 4, 3), (10, 20, 30), dtype=np.uint8)
    alpha = np.zeros((4, 4), dtype=np.uint8)
    alpha[:, :2] = 255

    result = detect_palette(image, alpha, tolerance=5.0, max_colors=None, max_unique_colors=512)

    assert len(result.groups) == 1
    assert result.groups[0].pixel_count == 8
    assert result.transparent_pixel_count == 8


def test_detect_palette_partial_alpha_pixels_count_as_color_but_are_flagged():
    image = np.full((4, 4, 3), (10, 20, 30), dtype=np.uint8)
    alpha = np.full((4, 4), 128, dtype=np.uint8)

    result = detect_palette(image, alpha, tolerance=5.0, max_colors=None, max_unique_colors=512)

    assert len(result.groups) == 1
    assert result.groups[0].has_partial_alpha is True
    assert result.groups[0].pixel_count == 16
    assert result.transparent_pixel_count == 0


def test_detect_palette_opaque_group_is_not_flagged_as_partial_alpha():
    image = np.full((4, 4, 3), (10, 20, 30), dtype=np.uint8)
    alpha = np.full((4, 4), 255, dtype=np.uint8)

    result = detect_palette(image, alpha, tolerance=5.0, max_colors=None, max_unique_colors=512)

    assert result.groups[0].has_partial_alpha is False


def test_detect_palette_without_alpha_channel_treats_every_pixel_as_opaque():
    image = np.full((3, 3, 3), (1, 2, 3), dtype=np.uint8)

    result = detect_palette(image, alpha=None, tolerance=5.0, max_colors=None, max_unique_colors=512)

    assert result.transparent_pixel_count == 0
    assert result.groups[0].pixel_count == 9


# --- Muchos colores ---
#
# "Muchos colores" no está cuantificado por spec.md ("Ambigüedades
# detectadas": "el implementador elige un valor razonable, ej. 50+"). Se usa
# una grilla 10x10 (100 píxeles) con un degradé bidimensional determinista
# que produce bastante más de 50 colores únicos, manteniendo el test rápido
# (el peor caso, tolerance=0.0, ejercita a pleno la fusión O(k^2) hacia
# `max_colors` sin volverse lento).


def _many_colors_gradient_image(size: int = 10) -> np.ndarray:
    xs = np.linspace(0, 255, size).astype(np.uint8)
    ys = np.linspace(0, 255, size).astype(np.uint8)
    blue = np.tile(xs, (size, 1))
    green = np.tile(ys.reshape(-1, 1), (1, size))
    red = ((blue.astype(np.int32) + green.astype(np.int32) * 7) % 256).astype(np.uint8)
    return np.stack([blue, green, red], axis=-1)


def test_many_colors_gradient_has_at_least_50_unique_source_colors():
    image = _many_colors_gradient_image()

    unique_count = len(np.unique(image.reshape(-1, 3), axis=0))

    assert unique_count >= 50


def test_detect_palette_many_colors_respects_max_colors_upper_bound():
    image = _many_colors_gradient_image()

    result = detect_palette(image, None, tolerance=0.0, max_colors=8, max_unique_colors=512)

    assert len(result.groups) <= 8
    assert sum(g.pixel_count for g in result.groups) == image.shape[0] * image.shape[1]


def test_detect_palette_many_colors_without_max_colors_keeps_most_of_them_separate_at_zero_tolerance():
    # No se exige igualdad exacta con la cantidad de colores BGR únicos: la
    # cuantización a 8 bits de OpenCV al convertir a Lab puede, en casos
    # puntuales, mapear dos BGR distintos al mismo triplete Lab -- pero con
    # tolerancia 0 la enorme mayoría debe seguir separada (no hay fusión
    # "amplia" posible).
    image = _many_colors_gradient_image()

    result = detect_palette(image, None, tolerance=0.0, max_colors=None, max_unique_colors=512)

    unique_count = len(np.unique(image.reshape(-1, 3), axis=0))
    assert len(result.groups) >= unique_count - 5


# --- extract_unique_colors: salvaguarda de rendimiento ---


def test_extract_unique_colors_reduces_cardinality_when_exceeding_budget():
    image = _many_colors_gradient_image()
    pixels = image.reshape(-1, 3)

    colors, inverse, counts = extract_unique_colors(pixels, max_unique_colors=8)

    assert len(colors) <= 8
    assert inverse.shape[0] == pixels.shape[0]
    assert int(counts.sum()) == pixels.shape[0]


def test_extract_unique_colors_is_a_passthrough_when_within_budget():
    image = _solid_block_image()
    pixels = image.reshape(-1, 3)

    colors, inverse, counts = extract_unique_colors(pixels, max_unique_colors=512)

    assert len(colors) == 3
    assert int(counts.sum()) == pixels.shape[0]


# --- build_quantized_preview ---


def test_build_quantized_preview_paints_transparent_where_no_group_assigned():
    image = np.full((4, 4, 3), (10, 20, 30), dtype=np.uint8)
    alpha = np.zeros((4, 4), dtype=np.uint8)
    alpha[:, :2] = 255

    result = detect_palette(image, alpha, tolerance=5.0, max_colors=None, max_unique_colors=512)
    preview = build_quantized_preview(result)

    assert preview.shape == (4, 4, 4)
    assert (preview[:, :2, 3] == 255).all()
    assert (preview[:, 2:, 3] == 0).all()


def test_build_quantized_preview_paints_each_group_with_its_representative_color():
    image = _solid_block_image()
    result = detect_palette(image, None, 5.0, None, 512)

    preview = build_quantized_preview(result)

    for group in result.groups:
        rows, cols = np.nonzero(group.mask == 255)
        sample_pixel = preview[rows[0], cols[0]]
        b, g, r = group.color_bgr
        assert tuple(int(c) for c in sample_pixel) == (b, g, r, 255)
