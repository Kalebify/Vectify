"""M2-S06: "Unión física de piezas" -- a diferencia de M2-S05 (agrupar,
lógico, nunca toca geometría), este módulo SÍ modifica geometría real: funde
2+ componentes físicos YA calculados por M2-S03
(app.core.component_analysis) en una única pieza fabricable, usando
operaciones booleanas (piezas solapadas/tangentes) y/o "bridging" simple
(piezas separadas -- un puente de conexión recto/directo entre los puntos
más cercanos de cada pieza, NUNCA un algoritmo de posicionamiento óptimo:
eso es MVP3, ver "Fuera de alcance" de spec.md).

Reutiliza:
- app.core.svg_path_parsing (collect_document_subpaths/tokenize_path_d/
  extract_subpaths/is_supported_path): misma infraestructura de tokenización
  ya compartida con el Laser Checker (M1-S08) y el análisis de componentes
  (M2-S03) -- una sola fuente de verdad, ver esos módulos.
- app.core.component_analysis.analyze_svg_components: se llama DOS veces --
  antes (para saber cuántas piezas hay en total y a qué componente
  pertenece cada subpath seleccionado) y, crucialmente, DESPUÉS de generar
  el resultado (sobre el SVG YA modificado) para la validación post-
  operación no negociable de spec.md: "Nunca fingir unión si las piezas
  siguen desconectadas". Si el conteo de componentes tras la unión no
  coincide EXACTAMENTE con el esperado (las piezas no seleccionadas intactas
  + exactamente 1 pieza nueva fusionada a partir de las seleccionadas), la
  operación se rechaza por completo (PhysicalUnionImpossibleError) -- nunca
  se devuelve un resultado que "parece" unido pero no lo está.

Librería de geometría booleana elegida: **Shapely** (bindings Python
maduros y activamente mantenidos sobre GEOS), no Clipper2/pyclipper -- ver
la justificación completa en IMPL.md del sprint. Resumen: pyclipper (el
único binding Python de Clipper disponible en este momento) envuelve
Clipper 1.x, no Clipper2, y no modela nativamente "polígono con agujeros"
ni expone distancia/`nearest_points`/validez de geometría -- hubiera
significado reimplementar a mano gran parte de lo que Shapely ya da
correcto y testeado (unión con agujeros preservados, distancia exacta
entre geometrías para decidir bridging, validación de geometría de
entrada). Shapely es la dependencia de geometría computacional de facto en
el ecosistema Python (GEOS es el mismo motor C++ que usa PostGIS/QGIS).

Algoritmo (por cada llamada a `union_selected_components`):

1. Se calcula `component_count_before` con analyze_svg_components sobre el
   SVG de entrada TAL CUAL llegó (antes de tocar nada).
2. Para cada componente seleccionado (ya agrupado por M2-S03: sus propios
   subpaths sólidos/agujero YA se sabe que son la MISMA pieza física), se
   construye su geometría real: unión de sus subpaths sólidos MENOS la
   unión de sus subpaths agujero (Shapely `Polygon`/`unary_union`/
   `difference`) -- se preservan los agujeros correctamente, nunca se
   "rellenan" por error.
3. Se arma un grafo COMPLETO entre las N piezas seleccionadas (distancia
   real Shapely entre cada par) y se calcula su Árbol de Expansión Mínima
   (Prim) -- ver "Ambigüedades detectadas" de spec.md, "selección parcial":
   esto conecta TODAS las piezas seleccionadas entre sí con el mínimo total
   de bridging necesario, usando siempre la pieza más cercana como
   referencia para cada bridge, en vez de rechazar la operación si alguna
   queda lejos de las demás.
4. Por cada arista del árbol: si la distancia real ya está dentro de la
   tolerancia de "tocarse" (`touch_ratio`, el MISMO criterio de M2-S03), no
   se agrega geometría extra (la unión booleana ya las funde). Si no, se
   agrega un "bridge": un rectángulo recto entre los puntos más cercanos de
   cada pieza (`shapely.ops.nearest_points`), extendido levemente hacia
   adentro de cada pieza para garantizar solape real (no solo tangencia
   numérica) -- deliberadamente el bridge MÁS simple posible (sin optimizar
   ancho, posición ni ruta), ver "Fuera de alcance" de spec.md.
5. Se unen (Shapely `unary_union`) las geometrías de las piezas
   seleccionadas más los bridges generados, se serializa el resultado de
   vuelta a comandos SVG M/L/Z (cada anillo -- exterior o agujero -- como su
   propio subpath dentro de un único `<path fill-rule="evenodd">` nuevo,
   fill-rule que hace que el criterio de "adentro/afuera" sea el mismo de
   paridad par/impar que ya usa `component_analysis`, sin depender del
   sentido de recorrido que Shapely le haya dado a cada anillo) y se
   reconstruye el documento: los subpaths de las piezas seleccionadas se
   quitan de sus `<path>` de origen (el `<path>` se elimina por completo si
   quedó vacío; si tenía OTROS subpaths no seleccionados -- ej. topología
   "stacked" con piezas no relacionadas en el mismo `<path>`, ver
   app.core.vector_engine -- esos se preservan intactos) y se agrega el
   nuevo `<path>` fusionado al final del documento.
6. Validación post-operación NO OPCIONAL: se corre analyze_svg_components
   de nuevo sobre el SVG YA modificado. Si el conteo resultante no es
   EXACTAMENTE `component_count_before - len(selections) + 1`, se lanza
   PhysicalUnionImpossibleError -- el caller (PhysicalUnionService) NUNCA
   persiste un SVG que llegó a esta función si esto ocurre.

Funciones puras y deterministas: mismo SVG + misma selección + mismas
tolerancias -> mismo resultado, sin tocar disco ni red (mismo criterio que
el resto de app.core).
"""

import math
import xml.etree.ElementTree as ET

from shapely.geometry import Polygon
from shapely.geometry.base import BaseGeometry
from shapely.ops import nearest_points, unary_union

from app.core.component_analysis import analyze_svg_components
from app.core.errors import InvalidParametersError, PhysicalUnionImpossibleError, PhysicalUnionInvalidGeometryError
from app.core.svg_path_parsing import (
    Point,
    collect_document_subpaths,
    extract_subpaths,
    is_supported_path,
    local_name,
    tokenize_path_d,
)
from app.core.svg_processing import compute_svg_stats, sanitize_svg

_MIN_RING_POINTS = 3
_MIN_AREA = 1e-9


def union_selected_components(
    sanitized_svg: str,
    selections: list[dict],
    touch_ratio: float,
    tiny_area_ratio: float,
    bridge_width_ratio: float,
    max_subpaths: int,
) -> dict:
    """Punto de entrada. `selections` es una lista de 2+ dicts
    `{"component_id": str, "members": [{"path_index": int, "subpath_index":
    int, "role": "solid"|"hole"}, ...]}` -- exactamente los `members` de
    cada `LayerComponent` YA calculado por M2-S03 que el caller (.NET)
    quiere fusionar; este módulo no vuelve a agrupar subpaths en
    componentes, confía en esa agrupación ya hecha (y la revalida
    igualmente al final, ver punto 6 del docstring del módulo).

    Devuelve un dict con `svg` (el documento YA modificado, sanitizado de
    nuevo), `component_count_before`, `component_count_after`,
    `expected_component_count_after`, `strategy`
    ("boolean_union"|"bridge"|"mixed") y `bridge_count`. Lanza
    InvalidParametersError (selección incoherente),
    PhysicalUnionInvalidGeometryError (geometría de entrada autointersectante/
    degenerada) o PhysicalUnionImpossibleError (la validación post-operación
    no negociable falló: nunca se devuelve -- ni se persiste del lado del
    caller -- un resultado que finge estar unido).
    """
    _validate_selections(selections)

    before = analyze_svg_components(sanitized_svg, touch_ratio, tiny_area_ratio, max_subpaths)
    component_count_before = len(before["components"])

    root = ET.fromstring(sanitized_svg)
    subpaths, _skipped = collect_document_subpaths(root)
    subpath_lookup = {(sp["path_index"], sp["subpath_index"]): sp["points"] for sp in subpaths}

    stats = compute_svg_stats(sanitized_svg)
    bounds = stats["bounds"]
    diagonal = math.hypot(bounds["width"], bounds["height"])
    if diagonal <= 0:
        raise PhysicalUnionInvalidGeometryError(
            "El SVG de origen no tiene geometría con área: no hay nada que unir físicamente."
        )

    touch_tolerance = touch_ratio * diagonal
    bridge_width = max(bridge_width_ratio * diagonal, touch_tolerance * 4, 1e-6)

    group_geoms: list[BaseGeometry] = []
    for selection in selections:
        group_geoms.append(_build_group_geometry(selection["members"], subpath_lookup, selection["component_id"]))

    mst_edges = _minimum_spanning_tree(group_geoms)

    bridges: list[Polygon] = []
    needs_bridge_count = 0
    for from_index, to_index, distance in mst_edges:
        if distance <= touch_tolerance:
            continue
        needs_bridge_count += 1
        bridge = _build_bridge(group_geoms[from_index], group_geoms[to_index], bridge_width)
        if bridge is not None:
            bridges.append(bridge)

    if needs_bridge_count == 0:
        strategy = "boolean_union"
    elif needs_bridge_count == len(mst_edges):
        strategy = "bridge"
    else:
        strategy = "mixed"

    merged = unary_union([*group_geoms, *bridges])
    polygons = _flatten_polygons(merged)
    if not polygons:
        raise PhysicalUnionImpossibleError(
            "La unión de las piezas seleccionadas no produjo ninguna geometría con área; "
            "no fue geométricamente posible unirlas."
        )

    _rebuild_document(root, selections, subpath_lookup, polygons)

    merged_svg = sanitize_svg(ET.tostring(root, encoding="unicode"))

    after = analyze_svg_components(merged_svg, touch_ratio, tiny_area_ratio, max_subpaths)
    component_count_after = len(after["components"])
    expected_after = component_count_before - len(selections) + 1

    if component_count_after != expected_after:
        raise PhysicalUnionImpossibleError(
            f"Tras la unión, el análisis de componentes detectó {component_count_after} pieza(s) en "
            f"total; se esperaban {expected_after} (las {component_count_before - len(selections)} "
            f"pieza(s) no seleccionada(s) sin cambios, más exactamente 1 pieza fusionada a partir de "
            f"las {len(selections)} piezas seleccionadas). La unión booleana/bridging simple disponible "
            "no logró conectar realmente todas las piezas seleccionadas en una sola pieza física (o "
            "fusionó de más con una pieza no seleccionada); no se persiste ningún resultado."
        )

    return {
        "svg": merged_svg,
        "component_count_before": component_count_before,
        "component_count_after": component_count_after,
        "expected_component_count_after": expected_after,
        "strategy": strategy,
        "bridge_count": len(bridges),
    }


def _validate_selections(selections: list[dict]) -> None:
    if len(selections) < 2:
        raise InvalidParametersError("La unión física requiere seleccionar al menos 2 componentes.")

    component_ids = [selection.get("component_id") for selection in selections]
    if len(set(component_ids)) != len(component_ids):
        raise InvalidParametersError("La selección para unión física tiene componentIds repetidos.")

    seen_members: set[tuple[int, int]] = set()
    for selection in selections:
        members = selection.get("members") or []
        if not members:
            raise InvalidParametersError(
                f"El componente '{selection.get('component_id')}' no tiene subpaths para unir."
            )
        for member in members:
            key = (member["path_index"], member["subpath_index"])
            if key in seen_members:
                raise InvalidParametersError(
                    f"El subpath (path_index={key[0]}, subpath_index={key[1]}) está referenciado por "
                    "más de un componente seleccionado."
                )
            seen_members.add(key)


def _build_group_geometry(members: list[dict], subpath_lookup: dict, component_id: str) -> BaseGeometry:
    solids: list[Polygon] = []
    holes: list[Polygon] = []

    for member in members:
        key = (member["path_index"], member["subpath_index"])
        points = subpath_lookup.get(key)
        if points is None:
            raise InvalidParametersError(
                f"El componente '{component_id}' referencia el subpath (path_index={key[0]}, "
                f"subpath_index={key[1]}), que no existe o no es analizable en el SVG de origen."
            )

        polygon = _build_valid_polygon(points, component_id)
        (holes if member["role"] == "hole" else solids).append(polygon)

    if not solids:
        raise PhysicalUnionInvalidGeometryError(
            f"El componente '{component_id}' no tiene ningún subpath sólido: no hay material que unir."
        )

    geom: BaseGeometry = unary_union(solids)
    if holes:
        geom = geom.difference(unary_union(holes))

    if geom.is_empty or geom.area <= _MIN_AREA:
        raise PhysicalUnionInvalidGeometryError(
            f"El componente '{component_id}' quedó sin área tras aplicar sus agujeros; no se puede "
            "procesar de forma segura para la unión física."
        )

    return geom


def _build_valid_polygon(points: list[Point], component_id: str) -> Polygon:
    """Construye un `Polygon` de Shapely y lo valida -- SIN intentar
    "arreglarlo" (ej. `buffer(0)`) si es inválido: una pieza autointersectante
    o degenerada se rechaza explícitamente, nunca se corrige en silencio (ver
    docstring del módulo/PhysicalUnionInvalidGeometryError -- criterio
    explícito de spec.md: "nunca fingir unión", extendido acá a "nunca
    fingir que la geometría de entrada estaba bien")."""
    if len(points) < _MIN_RING_POINTS:
        raise PhysicalUnionInvalidGeometryError(
            f"El componente '{component_id}' tiene un subpath con menos de 3 puntos: no define un área "
            "que se pueda unir de forma segura."
        )

    polygon = Polygon(points)
    if not polygon.is_valid or polygon.area <= _MIN_AREA:
        raise PhysicalUnionInvalidGeometryError(
            f"El componente '{component_id}' tiene geometría autointersectante o degenerada que no se "
            "puede procesar de forma segura para la unión física."
        )

    return polygon


def _minimum_spanning_tree(geoms: list[BaseGeometry]) -> list[tuple[int, int, float]]:
    """Prim clásico O(n^2) sobre el grafo COMPLETO de distancias reales
    (Shapely) entre las N piezas seleccionadas -- n es la cantidad de
    componentes seleccionados por el usuario en una sola operación,
    siempre chica (selección manual), así que O(n^2) alcanza sin
    salvaguarda de rendimiento adicional. Conecta TODAS las piezas
    seleccionadas con el mínimo bridging total necesario -- ver
    "Ambigüedades detectadas" de spec.md, "selección parcial"."""
    n = len(geoms)
    in_tree = [False] * n
    in_tree[0] = True
    best_distance = [math.inf] * n
    best_source = [0] * n
    for i in range(1, n):
        best_distance[i] = geoms[0].distance(geoms[i])
        best_source[i] = 0

    edges: list[tuple[int, int, float]] = []
    for _ in range(n - 1):
        next_index = -1
        next_distance = math.inf
        for i in range(n):
            if not in_tree[i] and best_distance[i] < next_distance:
                next_distance = best_distance[i]
                next_index = i

        in_tree[next_index] = True
        edges.append((best_source[next_index], next_index, next_distance))

        for i in range(n):
            if in_tree[i]:
                continue
            distance = geoms[next_index].distance(geoms[i])
            if distance < best_distance[i]:
                best_distance[i] = distance
                best_source[i] = next_index

    return edges


def _build_bridge(geom_a: BaseGeometry, geom_b: BaseGeometry, bridge_width: float) -> Polygon | None:
    """Bridge SIMPLE/DIRECTO (deliberado, ver "Fuera de alcance" de
    spec.md): un rectángulo recto entre los puntos más cercanos de cada
    pieza, sin ningún tipo de optimización de posición/ancho/ruta -- eso es
    MVP3. Se extiende levemente MÁS ALLÁ de cada punto más cercano, hacia
    adentro de cada pieza, para garantizar solape real (área en común, no
    solo un punto de tangencia) incluso frente a error de punto flotante:
    `unary_union` funde geometrías que se INTERSECAN, tocarse en un único
    punto matemático no alcanza de forma confiable."""
    point_a, point_b = nearest_points(geom_a, geom_b)
    ax, ay, bx, by = point_a.x, point_a.y, point_b.x, point_b.y
    dx, dy = bx - ax, by - ay
    length = math.hypot(dx, dy)
    if length <= 1e-9:
        # Ya se tocan/solapan (distancia real ~0): no hace falta bridge
        # (no debería llegar acá si touch_tolerance > 0, pero es una
        # salvaguarda defensiva sin costo).
        return None

    ux, uy = dx / length, dy / length
    perpendicular_x, perpendicular_y = -uy, ux
    half_width = bridge_width / 2.0
    extend = max(bridge_width, length * 0.05, 1e-6)

    start_x, start_y = ax - ux * extend, ay - uy * extend
    end_x, end_y = bx + ux * extend, by + uy * extend

    corners = [
        (start_x + perpendicular_x * half_width, start_y + perpendicular_y * half_width),
        (end_x + perpendicular_x * half_width, end_y + perpendicular_y * half_width),
        (end_x - perpendicular_x * half_width, end_y - perpendicular_y * half_width),
        (start_x - perpendicular_x * half_width, start_y - perpendicular_y * half_width),
    ]
    return Polygon(corners)


def _flatten_polygons(geom: BaseGeometry) -> list[Polygon]:
    if geom.is_empty:
        return []

    geom_type = geom.geom_type
    if geom_type == "Polygon":
        return [geom] if geom.area > _MIN_AREA else []
    if geom_type in ("MultiPolygon", "GeometryCollection"):
        flattened: list[Polygon] = []
        for member in geom.geoms:
            flattened.extend(_flatten_polygons(member))
        return flattened

    # Líneas/puntos degenerados (posibles en una GeometryCollection tras una
    # operación booleana con entradas al límite de la tolerancia): sin área,
    # se ignoran -- no aportan (ni quitan) material.
    return []


def _iter_paths_with_index(root: ET.Element):
    path_index = 0
    for element in root.iter():
        if local_name(element.tag) != "path":
            continue
        yield path_index, element
        path_index += 1


def _namespace(tag: str) -> str | None:
    if tag.startswith("{"):
        return tag[1:].split("}", 1)[0]
    return None


def _template_attrib(path_by_index: dict[int, ET.Element], path_index: int) -> dict[str, str]:
    element = path_by_index.get(path_index)
    if element is None:
        return {}
    return {key: value for key, value in element.attrib.items() if local_name(key) not in {"d", "transform", "id"}}


def _rebuild_document(
    root: ET.Element,
    selections: list[dict],
    subpath_lookup: dict,
    polygons: list[Polygon],
) -> None:
    """Muta `root` en el lugar: quita de sus `<path>` de origen los subpaths
    de las piezas seleccionadas (preservando cualquier OTRO subpath no
    seleccionado que compartiera el mismo `<path>`) y agrega un único
    `<path fill-rule="evenodd">` nuevo con la geometría fusionada, en
    coordenadas ABSOLUTAS (sin `transform`: ya vienen resueltas por
    `collect_document_subpaths`)."""
    removed_by_path: dict[int, set[int]] = {}
    for selection in selections:
        for member in selection["members"]:
            removed_by_path.setdefault(member["path_index"], set()).add(member["subpath_index"])

    parent_map = {child: parent for parent in root.iter() for child in parent}
    path_by_index = dict(_iter_paths_with_index(root))

    first_path_index = selections[0]["members"][0]["path_index"]
    template_attrib = _template_attrib(path_by_index, first_path_index)

    for path_index, element in path_by_index.items():
        removed_subpaths = removed_by_path.get(path_index)
        if not removed_subpaths:
            continue

        commands = tokenize_path_d(element.attrib.get("d", ""))
        if not is_supported_path(commands):
            # No debería ocurrir (los subpaths seleccionados vinieron de
            # collect_document_subpaths, que ya excluye paths no
            # soportados) -- salvaguarda defensiva, se deja intacto.
            continue

        local_subpaths = extract_subpaths(commands)
        kept = [subpath for index, subpath in enumerate(local_subpaths) if index not in removed_subpaths]

        if kept:
            element.attrib["d"] = "".join(_format_subpath(subpath["points"], subpath["closed"]) for subpath in kept)
        else:
            parent = parent_map.get(element)
            if parent is not None:
                parent.remove(element)

    svg_namespace = _namespace(root.tag)
    new_tag = f"{{{svg_namespace}}}path" if svg_namespace else "path"
    new_element = ET.Element(new_tag, template_attrib)
    new_element.attrib["fill-rule"] = "evenodd"
    new_element.attrib["d"] = "".join(_format_polygon(polygon) for polygon in polygons)
    root.append(new_element)


def _format_subpath(points: list[Point], closed: bool) -> str:
    if not points:
        return ""
    coords = [f"{x:.3f},{y:.3f}" for x, y in points]
    d = f"M{coords[0]}"
    if len(coords) > 1:
        d += "L" + "L".join(coords[1:])
    if closed:
        d += "Z"
    return d


def _format_polygon(polygon: Polygon) -> str:
    rings = [list(polygon.exterior.coords)[:-1]] + [list(interior.coords)[:-1] for interior in polygon.interiors]
    return "".join(_format_ring(ring) for ring in rings if len(ring) >= _MIN_RING_POINTS)


def _format_ring(points: list[tuple[float, float]]) -> str:
    coords = [f"{x:.3f},{y:.3f}" for x, y in points]
    d = f"M{coords[0]}"
    if len(coords) > 1:
        d += "L" + "L".join(coords[1:])
    d += "Z"
    return d
