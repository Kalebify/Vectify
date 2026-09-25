"""Laser Checker #1 (M1-S08): "Paths abiertos y líneas duplicadas".

Análisis de SOLO LECTURA sobre un SVG YA vectorizado (M1-S05) o simplificado
(M1-S07): nunca modifica el SVG de entrada ni produce uno de salida --
únicamente reporta problemas geométricos que pueden producir cortes láser
inesperados. Detecta dos clases de problema:

1. "Paths que deberían estar cerrados pero no lo están" (`open_path`): un
   subpath SIN comando `Z` explícito cuyo primer y último punto están a una
   distancia menor o igual a una tolerancia -- `close_gap_ratio`, fracción
   de la diagonal del bounding box de TODO el SVG (mismo criterio de
   tolerancia relativa que `app.core.simplification_pipeline.epsilon_ratio`
   de M1-S07, así escala con el tamaño del diseño en vez de ser un valor
   absoluto en píxeles). Se excluyen subpaths de menos de 3 puntos (una
   línea de 2 puntos o un punto degenerado no define un área que tenga
   sentido "cerrar" -- ver `_MIN_POINTS_FOR_CLOSURE_CHECK`, y el caso de
   falso positivo conocido documentado en tests/test_path_checker.py).

2. "Paths/segmentos duplicados o casi-duplicados" (`duplicate_path`): dos o
   más subpaths (del mismo `<path>` o de distintos) con la MISMA cantidad de
   puntos y el MISMO estado de cierre (abierto/cerrado), cuyos puntos
   -- comparados índice a índice, en el mismo sentido de recorrido o en el
   inverso (algunos subpaths se emiten con sentido de recorrido opuesto,
   ej. agujeros vs. contornos) -- están TODOS a una distancia menor o igual
   a una tolerancia -- `duplicate_point_ratio`, misma unidad relativa que
   `close_gap_ratio`. Los subpaths que se agrupan (directa o
   transitivamente) forman un único issue con todos sus miembros.

Mismo criterio de "no asumir semántica que no entendemos" que
app.core.simplification_pipeline: solo se analizan `<path>` cuyos comandos
son ESTRICTAMENTE M/L/Z absolutos en mayúscula (ver
app.core.svg_path_parsing.is_supported_path). Cualquier `<path>` con otros
comandos (H/V/C/S/Q/T/A, o cualquier variante en minúscula) se excluye por
completo del análisis -- ni se reporta un falso positivo/negativo sobre
datos que no se pueden interpretar con seguridad, ni se rompe nada (esto es
100% de solo lectura, nunca reescribe el `d` de ningún `<path>`).

Funciones puras y deterministas: mismo SVG + mismas tolerancias -> mismos
issues, en el mismo orden (recorrido en orden de documento: cada `<path>`
en el orden en que aparece, cada subpath en el orden en que aparece dentro
de su `<path>`) -- ver spec.md M1-S08, Definition of Done: "resultados
reproducibles".
"""

import math
import xml.etree.ElementTree as ET

from app.core.errors import TooManySubpathsError
from app.core.svg_path_parsing import Point, extract_subpaths, is_supported_path, local_name, tokenize_path_d
from app.core.svg_processing import compute_svg_stats

# Un subpath abierto de menos de 3 puntos (una línea de 2 puntos, o un único
# punto degenerado) no define una forma con área -- "cerrarlo" no tendría
# sentido geométrico, y flaguearlo sería un falso positivo sobre datos
# degenerados en vez de un problema real de diseño.
_MIN_POINTS_FOR_CLOSURE_CHECK = 3

# Tolerancia de punto flotante para decidir si un duplicado es "exacto"
# (distancia 0, o indistinguible de 0 por redondeo de coma flotante) vs.
# "casi-duplicado" (distancia > 0 pero dentro de la tolerancia configurada).
_EXACT_MATCH_EPSILON = 1e-9


def analyze_svg_paths(
    sanitized_svg: str,
    close_gap_ratio: float,
    duplicate_point_ratio: float,
    max_subpaths: int,
) -> dict:
    """Punto de entrada: recibe un SVG YA sanitizado (ver
    app.core.svg_processing.sanitize_svg) y las tolerancias relativas
    (fracción de la diagonal del bounding box del SVG completo -- ver
    docstring del módulo). `max_subpaths` es una salvaguarda de rendimiento:
    la detección de duplicados es O(n^2) sobre la cantidad de subpaths
    analizables, así que un diseño con una cantidad excesiva de subpaths
    lanza `TooManySubpathsError` en vez de arriesgar colgar el proceso.

    Devuelve un dict con `open_path_issues`, `duplicate_issues` (listas de
    dicts, ver `_detect_open_paths`/`_detect_duplicates`) y
    `skipped_path_count` (cantidad de `<path>` excluidos del análisis por
    contener comandos no soportados).
    """
    root = ET.fromstring(sanitized_svg)
    subpaths, skipped_path_count = _collect_subpaths(root)

    if len(subpaths) > max_subpaths:
        raise TooManySubpathsError(
            f"El SVG tiene {len(subpaths)} subpaths analizables, por encima del límite "
            f"permitido ({max_subpaths}) para el chequeo de paths abiertos/duplicados."
        )

    stats = compute_svg_stats(sanitized_svg)
    bounds = stats["bounds"]
    diagonal = math.hypot(bounds["width"], bounds["height"])

    if diagonal <= 0 or not subpaths:
        # SVG vacío o degenerado (sin paths, o un único punto): no hay
        # tolerancia relativa significativa que calcular, y nada que
        # analizar -- se devuelve sin issues en vez de dividir por cero o
        # usar una tolerancia absoluta sin sentido.
        return {"open_path_issues": [], "duplicate_issues": [], "skipped_path_count": skipped_path_count}

    close_gap_tolerance = close_gap_ratio * diagonal
    duplicate_tolerance = duplicate_point_ratio * diagonal

    return {
        "open_path_issues": _detect_open_paths(subpaths, close_gap_tolerance),
        "duplicate_issues": _detect_duplicates(subpaths, duplicate_tolerance),
        "skipped_path_count": skipped_path_count,
    }


def _distance(a: Point, b: Point) -> float:
    return math.hypot(a[0] - b[0], a[1] - b[1])


def _bounds_of(points: list[Point]) -> dict:
    xs = [p[0] for p in points]
    ys = [p[1] for p in points]
    return {"min_x": min(xs), "min_y": min(ys), "max_x": max(xs), "max_y": max(ys)}


def _collect_subpaths(root: ET.Element) -> tuple[list[dict], int]:
    """Recorre el documento en orden y devuelve la lista de subpaths
    analizables (de `<path>` con comandos soportados, ver docstring del
    módulo) junto con la cantidad de `<path>` excluidos. Cada subpath lleva
    `path_index` (índice del `<path>` en el documento, 0-based, CONTANDO
    también los excluidos -- así el índice que ve React coincide con el
    orden real de `<path>` del SVG que va a renderizar) y `subpath_index`
    (índice del subpath dentro de ese `<path>`, 0-based)."""
    subpaths: list[dict] = []
    skipped_path_count = 0
    path_index = 0

    for element in root.iter():
        if local_name(element.tag) != "path":
            continue

        d = element.attrib.get("d", "")
        commands = tokenize_path_d(d)
        if not is_supported_path(commands):
            skipped_path_count += 1
            path_index += 1
            continue

        for subpath_index, subpath in enumerate(extract_subpaths(commands)):
            points = subpath["points"]
            if len(points) < 2:
                # Subpath sin ningún segmento (un único `M` sin `L`
                # posteriores): no hay geometría que analizar.
                continue
            subpaths.append(
                {
                    "path_index": path_index,
                    "subpath_index": subpath_index,
                    "points": points,
                    "closed": subpath["closed"],
                    "bounds": _bounds_of(points),
                }
            )

        path_index += 1

    return subpaths, skipped_path_count


def _detect_open_paths(subpaths: list[dict], gap_tolerance: float) -> list[dict]:
    issues: list[dict] = []
    for subpath in subpaths:
        if subpath["closed"]:
            continue

        points = subpath["points"]
        if len(points) < _MIN_POINTS_FOR_CLOSURE_CHECK:
            continue

        gap = _distance(points[0], points[-1])
        if gap > gap_tolerance:
            continue

        issues.append(
            {
                "id": f"open-{subpath['path_index']}-{subpath['subpath_index']}",
                "type": "open_path",
                "severity": "error",
                "path_index": subpath["path_index"],
                "subpath_index": subpath["subpath_index"],
                "start_point": points[0],
                "end_point": points[-1],
                "gap_distance": gap,
                "bounds": subpath["bounds"],
            }
        )
    return issues


def _max_point_distance(a: list[Point], b: list[Point]) -> float:
    """Distancia punto a punto máxima entre dos listas de puntos de igual
    longitud, probando el mismo sentido de recorrido y el inverso (algunos
    subpaths se emiten con sentido opuesto -- ej. agujeros vs. contornos --
    sin dejar de ser geométricamente el mismo contorno) y quedándose con el
    mejor (menor) de los dos. `math.inf` si las longitudes no coinciden."""
    if len(a) != len(b) or not a:
        return math.inf

    forward = max(_distance(pa, pb) for pa, pb in zip(a, b))
    backward = max(_distance(pa, pb) for pa, pb in zip(a, reversed(b)))
    return min(forward, backward)


def _detect_duplicates(subpaths: list[dict], tolerance: float) -> list[dict]:
    n = len(subpaths)
    parent = list(range(n))

    def find(x: int) -> int:
        while parent[x] != x:
            parent[x] = parent[parent[x]]
            x = parent[x]
        return x

    def union(a: int, b: int) -> None:
        ra, rb = find(a), find(b)
        if ra != rb:
            parent[ra] = rb

    for i in range(n):
        for j in range(i + 1, n):
            a, b = subpaths[i], subpaths[j]
            if a["closed"] != b["closed"] or len(a["points"]) != len(b["points"]):
                continue
            if _max_point_distance(a["points"], b["points"]) <= tolerance:
                union(i, j)

    # Agrupa por raíz de unión, PRESERVANDO el orden de documento (se itera
    # i en orden ascendente, así que cada lista de miembros ya queda
    # ordenada por (path_index, subpath_index) de aparición).
    clusters: dict[int, list[int]] = {}
    for i in range(n):
        clusters.setdefault(find(i), []).append(i)

    issues: list[dict] = []
    cluster_id = 0
    # Los clústeres se recorren ordenados por el índice de su primer
    # miembro -- determinismo/orden reproducible independiente del orden de
    # iteración de un dict (Python 3.7+ preserva orden de inserción, que acá
    # ya coincide, pero se ordena explícitamente para no depender de eso).
    for root_index in sorted(clusters, key=lambda r: clusters[r][0]):
        members_idx = clusters[root_index]
        if len(members_idx) < 2:
            continue

        cluster_id += 1
        max_distance = max(
            _max_point_distance(subpaths[i]["points"], subpaths[j]["points"])
            for pos, i in enumerate(members_idx)
            for j in members_idx[pos + 1 :]
        )
        exact = max_distance <= _EXACT_MATCH_EPSILON

        issues.append(
            {
                "id": f"dup-{cluster_id}",
                "type": "duplicate_path",
                "severity": "error" if exact else "warning",
                "exact": exact,
                "max_point_distance": max_distance,
                "members": [
                    {
                        "path_index": subpaths[i]["path_index"],
                        "subpath_index": subpaths[i]["subpath_index"],
                        "bounds": subpaths[i]["bounds"],
                    }
                    for i in members_idx
                ],
            }
        )
    return issues
