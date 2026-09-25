"""Tests de app.core.svg_processing: sanitización defensiva (sin <script>,
sin referencias externas, sin manejadores de eventos inline) y cálculo de
estadísticas (paths/nodos aproximados/bounds) sobre SVGs de ejemplo escritos
a mano -- no dependen de VTracer (eso lo cubre test_vector_engine.py). Ver
spec.md M1-S05, "Seguridad/robustez" y "Python/FastAPI".
"""

import pytest

from app.core.errors import InvalidSvgError
from app.core.svg_processing import compute_svg_stats, sanitize_svg

SIMPLE_SQUARE_SVG = (
    '<?xml version="1.0" encoding="UTF-8"?>'
    '<svg xmlns="http://www.w3.org/2000/svg" width="50" height="50">'
    '<path d="M10,10 L40,10 L40,40 L10,40 Z" fill="#000000"/>'
    "</svg>"
)

RING_WITH_HOLE_SVG = (
    '<svg xmlns="http://www.w3.org/2000/svg" width="80" height="80">'
    '<path d="M10,10 L70,10 L70,70 L10,70 Z M30,30 L50,30 L50,50 L30,50 Z" '
    'fill="#000000" transform="translate(5,5)"/>'
    "</svg>"
)

EMPTY_SVG = '<svg xmlns="http://www.w3.org/2000/svg" width="40" height="40"></svg>'


def test_sanitize_svg_keeps_well_formed_content_intact():
    sanitized = sanitize_svg(SIMPLE_SQUARE_SVG)

    assert "<path" in sanitized
    assert 'd="M10,10 L40,10 L40,40 L10,40 Z"' in sanitized or "M10,10" in sanitized


def test_sanitize_svg_is_parseable_xml():
    import xml.etree.ElementTree as ET

    sanitized = sanitize_svg(SIMPLE_SQUARE_SVG)

    # No debería lanzar: el resultado siempre es XML válido por construcción.
    ET.fromstring(sanitized)


def test_sanitize_svg_removes_script_tags():
    malicious = (
        '<svg xmlns="http://www.w3.org/2000/svg" width="10" height="10">'
        "<script>alert('xss')</script>"
        '<path d="M0,0 L10,0 L10,10 L0,10 Z"/>'
        "</svg>"
    )

    sanitized = sanitize_svg(malicious)

    assert "<script" not in sanitized
    assert "alert" not in sanitized
    assert "<path" in sanitized


def test_sanitize_svg_removes_inline_event_handlers():
    malicious = (
        '<svg xmlns="http://www.w3.org/2000/svg" width="10" height="10">'
        '<path d="M0,0 L10,0 L10,10 L0,10 Z" onload="alert(1)" onclick="alert(2)"/>'
        "</svg>"
    )

    sanitized = sanitize_svg(malicious)

    assert "onload" not in sanitized
    assert "onclick" not in sanitized


def test_sanitize_svg_strips_external_href_but_keeps_internal_fragment_refs():
    malicious = (
        '<svg xmlns="http://www.w3.org/2000/svg" width="10" height="10">'
        '<a xmlns:xlink="http://www.w3.org/1999/xlink" xlink:href="https://evil.example/payload">'
        '<path d="M0,0 L10,0 L10,10 L0,10 Z"/>'
        "</a>"
        '<use href="#local-symbol"/>'
        "</svg>"
    )

    sanitized = sanitize_svg(malicious)

    assert "evil.example" not in sanitized
    assert 'href="#local-symbol"' in sanitized


def test_sanitize_svg_removes_foreign_object_and_iframe():
    malicious = (
        '<svg xmlns="http://www.w3.org/2000/svg" width="10" height="10">'
        "<foreignObject><iframe src=\"https://evil.example\"></iframe></foreignObject>"
        '<path d="M0,0 L10,0 L10,10 L0,10 Z"/>'
        "</svg>"
    )

    sanitized = sanitize_svg(malicious)

    assert "foreignObject" not in sanitized
    assert "iframe" not in sanitized
    assert "evil.example" not in sanitized


def test_sanitize_svg_removes_style_element_with_external_import():
    malicious = (
        '<svg xmlns="http://www.w3.org/2000/svg" width="10" height="10">'
        "<style>@import url(https://evil.example/x.css)</style>"
        '<path d="M0,0 L10,0 L10,10 L0,10 Z"/>'
        "</svg>"
    )

    sanitized = sanitize_svg(malicious)

    assert "<style" not in sanitized
    assert "evil.example" not in sanitized
    assert "<path" in sanitized


def test_sanitize_svg_strips_style_attribute_with_external_url():
    malicious = (
        '<svg xmlns="http://www.w3.org/2000/svg" width="10" height="10">'
        '<rect width="10" height="10" style="fill:url(https://evil.example/a.svg)"/>'
        "</svg>"
    )

    sanitized = sanitize_svg(malicious)

    assert "evil.example" not in sanitized
    assert "style=" not in sanitized


def test_sanitize_svg_keeps_style_attribute_with_internal_url_reference():
    benign = (
        '<svg xmlns="http://www.w3.org/2000/svg" width="10" height="10">'
        '<defs><linearGradient id="gradiente-interno"/></defs>'
        '<rect width="10" height="10" style="fill:url(#gradiente-interno)"/>'
        "</svg>"
    )

    sanitized = sanitize_svg(benign)

    assert 'style="fill:url(#gradiente-interno)"' in sanitized


def test_sanitize_svg_rejects_malformed_xml():
    with pytest.raises(InvalidSvgError):
        sanitize_svg("<svg><path d='M0,0'></svg-not-closed>")


def test_sanitize_svg_rejects_non_svg_root():
    with pytest.raises(InvalidSvgError):
        sanitize_svg('<html xmlns="http://www.w3.org/2000/svg"></html>')


def test_sanitize_svg_rejects_doctype_declarations():
    with pytest.raises(InvalidSvgError):
        sanitize_svg(
            '<?xml version="1.0"?><!DOCTYPE svg [<!ENTITY xxe SYSTEM "file:///etc/passwd">]>'
            '<svg xmlns="http://www.w3.org/2000/svg"><path d="M0,0"/></svg>'
        )


def test_compute_svg_stats_counts_single_path_and_its_nodes():
    stats = compute_svg_stats(SIMPLE_SQUARE_SVG)

    assert stats["path_count"] == 1
    assert stats["approx_node_count"] == 4  # M + 3 L


def test_compute_svg_stats_bounds_match_the_square_coordinates():
    stats = compute_svg_stats(SIMPLE_SQUARE_SVG)

    assert stats["bounds"] == {
        "min_x": 10.0,
        "min_y": 10.0,
        "max_x": 40.0,
        "max_y": 40.0,
        "width": 30.0,
        "height": 30.0,
    }


def test_compute_svg_stats_applies_translate_transform_to_bounds():
    stats = compute_svg_stats(RING_WITH_HOLE_SVG)

    # Outer subpath: 10..70 + translate(5,5) -> 15..75. Nested inner hole
    # subpath (30..50) is within that range, so it doesn't widen the bbox.
    assert stats["bounds"]["min_x"] == pytest.approx(15.0)
    assert stats["bounds"]["min_y"] == pytest.approx(15.0)
    assert stats["bounds"]["max_x"] == pytest.approx(75.0)
    assert stats["bounds"]["max_y"] == pytest.approx(75.0)


def test_compute_svg_stats_counts_nested_subpaths_as_one_path_with_more_nodes():
    stats = compute_svg_stats(RING_WITH_HOLE_SVG)

    # Un único <path> con dos subpaths (outer + hole): sigue siendo 1 path,
    # pero con más nodos que una figura simple -- ver spec.md, "Pruebas":
    # "topología con paths anidados/fill-rule".
    assert stats["path_count"] == 1
    assert stats["approx_node_count"] == 8  # 4 nodos del outer + 4 del hole


def test_compute_svg_stats_on_empty_svg_returns_zeroed_bounds_without_crashing():
    stats = compute_svg_stats(EMPTY_SVG)

    assert stats["path_count"] == 0
    assert stats["approx_node_count"] == 0
    assert stats["bounds"] == {
        "min_x": 0.0,
        "min_y": 0.0,
        "max_x": 0.0,
        "max_y": 0.0,
        "width": 0.0,
        "height": 0.0,
    }
