"""M2-S03: "Componentes independientes por capa" -- distingue, DENTRO de la
geometría SVG de una única capa vectorial de un solo color, cuántas piezas
FÍSICAS geométricamente separadas contiene (ej. "Azul: 3 piezas"). Análisis
de SOLO LECTURA (igual criterio que app.core.path_checker, M1-S08): nunca
modifica el SVG de entrada ni une/separa geometría -- unir piezas físicas es
una operación explícitamente distinta y posterior (M2-S06, fuera de alcance
acá).

Reutiliza EXACTAMENTE el mismo tokenizer/infraestructura de subpaths ya
compartida (app.core.svg_path_parsing: tokenización M/L/Z, extracción de
subpaths, resolución de `transform="translate(...)"`), la misma que ya usa
el Laser Checker de M1-S08 -- ver spec.md M2-S03, "Ambigüedades detectadas":
"lo más consistente es reutilizar ese mismo tokenizer/infraestructura en
Python".

Algoritmo (unión de subpaths en componentes físicos vía Union-Find, mismo
patrón que app.core.path_checker._detect_duplicates):

1. "Contención" (agujeros): para cada subpath se calcula su profundidad de
   anidamiento (`depth`) -- cuántos OTROS subpaths contienen geométricamente
   un punto representativo suyo (ray casting/point-in-polygon). Un subpath
   con profundidad IMPAR es un AGUJERO (regla de paridad even-odd, la misma
   que usa el propio renderizado SVG: profundidad par = sólido, impar =
   hueco) y se UNE a su padre DIRECTO (el subpath contenedor de área más
   chica, no cualquier ancestro) -- así "un anillo con un disco suelto
   adentro de su agujero" NO se fusiona con el anillo (el disco está a
   profundidad par, es una pieza física separada, a menos que además
   TOQUE al anillo), pero "una arandela" (agujero directo dentro de su
   propio contorno) sí queda como un único componente -- ver spec.md,
   criterio de aceptación: "un subpath que es un AGUJERO... pertenece al
   MISMO componente".

2. "Tocarse": dos subpaths (sin importar su relación de anidamiento) cuya
   distancia mínima segmento-a-segmento es <= `touch_ratio` (fracción de la
   diagonal del bounding box de TODO el SVG, mismo criterio de tolerancia
   relativa que CheckParams de M1-S08) se consideran la MISMA pieza física
   al cortar, y se unen -- ver spec.md, "Ambigüedades detectadas": "el
   implementador decide ... probablemente: comparten al menos un punto
   exacto o dentro de tolerancia -> mismo componente físico".

El área de cada componente es la suma de las áreas (shoelace) de sus
miembros SÓLIDOS menos la de sus miembros AGUJERO (misma regla de paridad
que 1) -- área NETA de material, no la suma bruta de todos los subpaths.
Los IDs (`component-1`, `component-2`, ...) son estables DENTRO de una
versión: se asignan en el orden de aparición del primer miembro de cada
grupo (mismo `path_index`/`subpath_index` de aparición en el documento),
determinista para el mismo SVG + mismos parámetros -- ver spec.md, criterio
de aceptación: "mismo layer + mismos parámetros -> mismos IDs, mismo orden".

Funciones puras y deterministas: mismo SVG + mismas tolerancias -> mismo
resultado, sin tocar disco ni red (mismo criterio que app.core.path_checker).
"""

import math
import xml.etree.ElementTree as ET

from app.core.errors import TooManySubpathsForComponentsError
from app.core.svg_path_parsing import Point, collect_document_subpaths
from app.core.svg_processing import compute_svg_stats

# Un subpath de menos de 3 puntos (una línea de 2 puntos) no define un área
# ni una forma cerrada que pueda "contener" nada -- se sigue reportando como
# componente propio (con área 0), pero nunca participa como candidato a
# contenedor/agujero de otro subpath.
_MIN_POINTS_FOR_AREA = 3


def analyze_svg_components(
    sanitized_svg: str,
    touch_ratio: float,
    tiny_area_ratio: float,
    max_subpaths: int,
) -> dict:
    """Punto de entrada: recibe un SVG YA sanitizado (ver
    app.core.svg_processing.sanitize_svg) de UNA capa vectorial de un solo
    color, y las tolerancias relativas (`touch_ratio`: fracción de la
    diagonal del bounding box del SVG completo; `tiny_area_ratio`: fracción
    del área total del bounding box del SVG completo). `max_subpaths` es una
    salvaguarda de rendimiento: tanto la detección de contención como la de
    contacto son O(n^2) sobre la cantidad de subpaths analizables, así que un
    diseño con una cantidad excesiva lanza `TooManySubpathsForComponentsError`
    en vez de arriesgar colgar el proceso.

    Devuelve un dict con `components` (lista de dicts, ver
    `_build_component`) y `skipped_path_count` (cantidad de `<path>`
    excluidos del análisis por contener comandos/transforms no soportados).
    """
    root = ET.fromstring(sanitized_svg)
    subpaths, skipped_path_count = collect_document_subpaths(root)

    if len(subpaths) > max_subpaths:
        raise TooManySubpathsForComponentsError(
            f"El SVG tiene {len(subpaths)} subpaths analizables, por encima del límite "
            f"permitido ({max_subpaths}) para el análisis de componentes físicos."
        )

    if not subpaths:
        return {"components": [], "skipped_path_count": skipped_path_count}

    stats = compute_svg_stats(sanitized_svg)
    bounds = stats["bounds"]
    diagonal = math.hypot(bounds["width"], bounds["height"])
    total_area = bounds["width"] * bounds["height"]

    touch_tolerance = touch_ratio * diagonal if diagonal > 0 else 0.0
    tiny_area_threshold = tiny_area_ratio * total_area if total_area > 0 else 0.0

    groups = _group_into_components(subpaths, touch_tolerance)
    depths = groups.depths

    components = []
    for component_index, member_indices in enumerate(groups.ordered_groups, start=1):
        components.append(
            _build_component(f"component-{component_index}", subpaths, depths, member_indices, tiny_area_threshold)
        )

    return {"components": components, "skipped_path_count": skipped_path_count}


class _GroupingResult:
    def __init__(self, depths: list[int], ordered_groups: list[list[int]]) -> None:
        self.depths = depths
        self.ordered_groups = ordered_groups


def _group_into_components(subpaths: list[dict], touch_tolerance: float) -> _GroupingResult:
    n = len(subpaths)
    depths = _compute_depths(subpaths)
    parents = _compute_direct_parents(subpaths, depths)

    parent_uf = list(range(n))

    def find(x: int) -> int:
        while parent_uf[x] != x:
            parent_uf[x] = parent_uf[parent_uf[x]]
            x = parent_uf[x]
        return x

    def union(a: int, b: int) -> None:
        ra, rb = find(a), find(b)
        if ra != rb:
            parent_uf[ra] = rb

    # 1) Contención: un agujero (profundidad impar) se une a su padre
    # DIRECTO -- ver docstring del módulo.
    for i in range(n):
        if depths[i] % 2 == 1 and parents[i] is not None:
            union(i, parents[i])

    # 2) Contacto: subpaths (de cualquier profundidad/relación) cuya
    # distancia mínima segmento-a-segmento está dentro de la tolerancia se
    # consideran la misma pieza física -- con un descarte rápido por
    # bounding box (expandido por la tolerancia) antes del cálculo costoso
    # de distancia real, para que el caso común (formas lejos entre sí) sea
    # barato.
    for i in range(n):
        for j in range(i + 1, n):
            if find(i) == find(j):
                continue
            if not _bounds_could_touch(subpaths[i]["bounds"], subpaths[j]["bounds"], touch_tolerance):
                continue
            if (
                _polyline_distance(
                    subpaths[i]["points"], subpaths[i]["closed"], subpaths[j]["points"], subpaths[j]["closed"]
                )
                <= touch_tolerance
            ):
                union(i, j)

    groups: dict[int, list[int]] = {}
    for i in range(n):
        groups.setdefault(find(i), []).append(i)

    # Orden reproducible e independiente del orden de iteración de un dict:
    # cada grupo se ordena por el índice de su primer miembro de aparición
    # (mismo criterio que app.core.path_checker._detect_duplicates).
    ordered_groups = [groups[root] for root in sorted(groups, key=lambda r: groups[r][0])]
    return _GroupingResult(depths, ordered_groups)


def _build_component(
    component_id: str, subpaths: list[dict], depths: list[int], member_indices: list[int], tiny_area_threshold: float
) -> dict:
    members = []
    net_area = 0.0
    min_x = min_y = math.inf
    max_x = max_y = -math.inf

    for idx in member_indices:
        subpath = subpaths[idx]
        role = "hole" if depths[idx] % 2 == 1 else "solid"
        area = _polygon_area(subpath["points"])
        net_area += -area if role == "hole" else area

        b = subpath["bounds"]
        min_x, min_y = min(min_x, b["min_x"]), min(min_y, b["min_y"])
        max_x, max_y = max(max_x, b["max_x"]), max(max_y, b["max_y"])

        members.append(
            {
                "path_index": subpath["path_index"],
                "subpath_index": subpath["subpath_index"],
                "role": role,
                "bounds": b,
                "area": area,
            }
        )

    net_area = max(net_area, 0.0)

    return {
        "id": component_id,
        "members": members,
        "bounds": {"min_x": min_x, "min_y": min_y, "max_x": max_x, "max_y": max_y},
        "area": net_area,
        "is_tiny": net_area <= tiny_area_threshold,
    }


def _inward_point(points: list[Point]) -> Point:
    """Punto de prueba GARANTIZADO dentro del propio subpath, usado para la
    detección de contención -- deliberadamente NO el centroide: contornos
    CONCÉNTRICOS (ej. un anillo y su propio agujero, o un anillo con una
    pieza suelta centrada dentro de su agujero -- casos comunes en este
    dominio, piezas de corte láser) comparten el mismo centroide aritmético,
    lo que volvería AMBIGUA (o directamente invertida) la relación de
    contención si se usara el centroide de cada uno como punto de prueba
    (el centroide del contorno EXTERIOR cae, por simetría, tan "adentro" del
    agujero como el propio centroide del agujero).

    En cambio, se toma el punto medio del primer borde con longitud no nula
    del polígono, empujado levemente hacia el interior (a lo largo de la
    normal del borde, una fracción chica de su longitud) -- probando con
    ray casting sobre el PROPIO polígono cuál de los dos lados de la normal
    cae adentro. Este punto queda pegado al borde de este subpath
    específico, lejos de cualquier contorno anidado más chico (que por
    definición ocupa una región más interior, con margen respecto al borde
    de este), así que no se confunde con la región de un subpath nested más
    profundo. Si un borde resulta degenerado (longitud ~0) o
    patológicamente cóncavo, se prueba con el siguiente borde; si ninguno
    funciona (no debería ocurrir para un polígono simple válido), cae de
    vuelta al centroide."""
    n = len(points)
    for i in range(n):
        p0, p1 = points[i], points[(i + 1) % n]
        edge_dx, edge_dy = p1[0] - p0[0], p1[1] - p0[1]
        edge_length = math.hypot(edge_dx, edge_dy)
        if edge_length <= 1e-9:
            continue

        mid = ((p0[0] + p1[0]) / 2.0, (p0[1] + p1[1]) / 2.0)
        normal = (-edge_dy / edge_length, edge_dx / edge_length)
        nudge = edge_length * 1e-3

        for sign in (1.0, -1.0):
            candidate = (mid[0] + normal[0] * nudge * sign, mid[1] + normal[1] * nudge * sign)
            if _point_in_polygon(candidate, points):
                return candidate

    return (sum(p[0] for p in points) / n, sum(p[1] for p in points) / n)


def _point_in_bounds(point: Point, bounds: dict) -> bool:
    x, y = point
    return bounds["min_x"] <= x <= bounds["max_x"] and bounds["min_y"] <= y <= bounds["max_y"]


def _point_in_polygon(point: Point, polygon: list[Point]) -> bool:
    """Ray casting estándar (par/impar de cruces): trata `polygon` como
    cerrado sin importar el flag `closed` del subpath (para propósitos de
    contención, la geometría de un contorno de VTracer siempre delimita un
    área, tenga o no comando `Z` explícito)."""
    x, y = point
    n = len(polygon)
    inside = False
    j = n - 1
    for i in range(n):
        xi, yi = polygon[i]
        xj, yj = polygon[j]
        if (yi > y) != (yj > y):
            x_intersect = (xj - xi) * (y - yi) / (yj - yi) + xi
            if x < x_intersect:
                inside = not inside
        j = i
    return inside


def _compute_depths(subpaths: list[dict]) -> list[int]:
    n = len(subpaths)
    reps = [_inward_point(sp["points"]) if len(sp["points"]) >= _MIN_POINTS_FOR_AREA else None for sp in subpaths]
    depths = [0] * n

    for i in range(n):
        rep = reps[i]
        if rep is None:
            continue
        count = 0
        for j in range(n):
            if i == j or len(subpaths[j]["points"]) < _MIN_POINTS_FOR_AREA:
                continue
            if not _point_in_bounds(rep, subpaths[j]["bounds"]):
                continue
            if _point_in_polygon(rep, subpaths[j]["points"]):
                count += 1
        depths[i] = count

    return depths


def _compute_direct_parents(subpaths: list[dict], depths: list[int]) -> list[int | None]:
    """Para cada subpath con profundidad > 0, el padre DIRECTO es -- entre
    los subpaths que lo contienen -- el de área más chica (el contorno más
    ajustado, no cualquier ancestro más grande de la cadena de anidamiento).
    """
    n = len(subpaths)
    reps = [_inward_point(sp["points"]) if len(sp["points"]) >= _MIN_POINTS_FOR_AREA else None for sp in subpaths]
    areas = [_polygon_area(sp["points"]) if len(sp["points"]) >= _MIN_POINTS_FOR_AREA else 0.0 for sp in subpaths]
    parents: list[int | None] = [None] * n

    for i in range(n):
        rep = reps[i]
        if rep is None or depths[i] == 0:
            continue
        best_j = None
        best_area = math.inf
        for j in range(n):
            if i == j or areas[j] <= 0.0:
                continue
            if not _point_in_bounds(rep, subpaths[j]["bounds"]):
                continue
            if areas[j] < best_area and _point_in_polygon(rep, subpaths[j]["points"]):
                best_area = areas[j]
                best_j = j
        parents[i] = best_j

    return parents


def _polygon_area(points: list[Point]) -> float:
    """Área (shoelace), siempre positiva -- el signo del winding no importa
    acá: la paridad de `depths` ya decide si un subpath suma o resta al área
    neta de su componente."""
    n = len(points)
    if n < _MIN_POINTS_FOR_AREA:
        return 0.0
    area = 0.0
    for i in range(n):
        x1, y1 = points[i]
        x2, y2 = points[(i + 1) % n]
        area += x1 * y2 - x2 * y1
    return abs(area) / 2.0


def _bounds_could_touch(bounds_a: dict, bounds_b: dict, tolerance: float) -> bool:
    """Descarte rápido O(1): si los bounding boxes (expandidos por
    `tolerance`) ni siquiera se superponen, los subpaths no pueden estar a
    distancia <= tolerance -- evita el cálculo O(segments^2) de
    `_polyline_distance` para la enorme mayoría de pares en un diseño típico
    (formas lejos entre sí)."""
    return not (
        bounds_a["max_x"] + tolerance < bounds_b["min_x"]
        or bounds_b["max_x"] + tolerance < bounds_a["min_x"]
        or bounds_a["max_y"] + tolerance < bounds_b["min_y"]
        or bounds_b["max_y"] + tolerance < bounds_a["min_y"]
    )


def _segments(points: list[Point], closed: bool) -> list[tuple[Point, Point]]:
    n = len(points)
    segs = [(points[i], points[i + 1]) for i in range(n - 1)]
    if closed and n >= 2:
        segs.append((points[-1], points[0]))
    return segs


def _clamp(value: float, lo: float, hi: float) -> float:
    return max(lo, min(hi, value))


def _point_segment_distance(p: Point, a: Point, b: Point) -> float:
    ax, ay = a
    bx, by = b
    px, py = p
    dx, dy = bx - ax, by - ay
    length_sq = dx * dx + dy * dy
    if length_sq <= 1e-18:
        return math.hypot(px - ax, py - ay)
    t = _clamp(((px - ax) * dx + (py - ay) * dy) / length_sq, 0.0, 1.0)
    cx, cy = ax + t * dx, ay + t * dy
    return math.hypot(px - cx, py - cy)


def _orientation(p: Point, q: Point, r: Point) -> int:
    val = (q[1] - p[1]) * (r[0] - q[0]) - (q[0] - p[0]) * (r[1] - q[1])
    if abs(val) < 1e-12:
        return 0
    return 1 if val > 0 else 2


def _on_segment(p: Point, q: Point, r: Point) -> bool:
    return (
        min(p[0], r[0]) - 1e-9 <= q[0] <= max(p[0], r[0]) + 1e-9
        and min(p[1], r[1]) - 1e-9 <= q[1] <= max(p[1], r[1]) + 1e-9
    )


def _segments_intersect(a: Point, b: Point, c: Point, d: Point) -> bool:
    o1, o2, o3, o4 = _orientation(a, b, c), _orientation(a, b, d), _orientation(c, d, a), _orientation(c, d, b)

    if o1 != o2 and o3 != o4:
        return True
    if o1 == 0 and _on_segment(a, c, b):
        return True
    if o2 == 0 and _on_segment(a, d, b):
        return True
    if o3 == 0 and _on_segment(c, a, d):
        return True
    if o4 == 0 and _on_segment(c, b, d):
        return True
    return False


def _segment_distance(a: Point, b: Point, c: Point, d: Point) -> float:
    """Distancia mínima entre dos segmentos (0.0 si se cruzan o se tocan)."""
    if _segments_intersect(a, b, c, d):
        return 0.0
    return min(
        _point_segment_distance(a, c, d),
        _point_segment_distance(b, c, d),
        _point_segment_distance(c, a, b),
        _point_segment_distance(d, a, b),
    )


def _polyline_distance(points_a: list[Point], closed_a: bool, points_b: list[Point], closed_b: bool) -> float:
    """Distancia mínima segmento-a-segmento entre dos polilíneas (el
    contorno completo de cada subpath, cerrado o no) -- criterio de
    "tocarse" de spec.md: comparten al menos un punto/borde dentro de la
    tolerancia. Se compara BORDE a borde (no solo vértice a vértice) para
    detectar correctamente el caso "un vértice de una forma cae sobre el
    borde de la otra, lejos de sus vértices" -- una aproximación
    punto-a-punto (como la de app.core.path_checker._detect_duplicates, que
    solo necesita comparar formas de igual cantidad de puntos) no alcanzaría
    acá, donde las dos formas pueden tener cantidades de vértices distintas."""
    best = math.inf
    for a1, a2 in _segments(points_a, closed_a):
        for b1, b2 in _segments(points_b, closed_b):
            distance = _segment_distance(a1, a2, b1, b2)
            if distance < best:
                best = distance
            if best <= 0.0:
                return 0.0
    return best
