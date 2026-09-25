"""Simplificación de nodos de un SVG ya vectorizado (M1-S07): reduce la
cantidad de puntos de cada subpath aplicando el algoritmo de Douglas-Peucker,
preservando la topología en los casos soportados (paths cerrados, agujeros/
subpaths anidados, orden/sentido de recorrido de cada subpath).

Algoritmo elegido: Douglas-Peucker. Es el estándar de facto para simplificar
polilíneas/contornos (usado por herramientas de cartografía y vectorización en
general), tiene una implementación simple y determinista, y -- a diferencia de
un muestreo/decimado uniforme -- conserva los puntos que definen la forma
visual (esquinas, curvas pronunciadas) mientras descarta los que están "casi
sobre la línea recta" entre sus vecinos. Se evaluó Visvalingam-Whyatt (basado
en área de triángulos) como alternativa; se descartó por ser más costoso de
razonar sobre la preservación de esquinas agudas (criterio de aceptación
explícito de spec.md M1-S07: "esquinas (ángulos agudos)") sin aportar una
ventaja clara para este caso de uso.

Alcance/limitación conocida y deliberada: el motor de trazado actual
(app.core.vector_engine.VtracerEngine, `mode="polygon"`) solo emite comandos
M/L/Z absolutos en mayúscula -- nunca curvas Bézier (`C`) ni comandos
relativos en minúscula. Esta simplificación SOLO sabe operar sobre esa forma
exacta (M/L/Z absolutos, mayúscula); si un <path> contiene cualquier otro
comando -- curvas (`C`,`S`,`Q`,`T`), arcos (`A`), líneas horizontales/
verticales (`H`,`V`), o CUALQUIER variante relativa en minúscula (incluyendo
`m`,`l`,`z`, ya que el pipeline no acumula offsets relativos) -- se lo deja
intacto tal cual llegó, en vez de arriesgar corromperlo -- prioriza "nunca
romper un path válido" (Definition of Done de spec.md) sobre simplificar
agresivamente cada caso posible.

Funciones puras y deterministas: mismo SVG + mismo epsilon_ratio -> mismo
resultado, sin tocar disco ni red (mismo criterio que app.core.svg_processing
y app.core.pipeline).
"""

import math
import re
import xml.etree.ElementTree as ET

from app.core.svg_processing import compute_svg_stats

Point = tuple[float, float]

# Reconoce TODAS las letras de comando de path SVG (mayúsculas y minúsculas:
# M,L,C,Z,H,V,S,Q,T,A) como límites de token, no solo las soportadas -- si el
# regex no reconociera una letra de comando como límite, sus argumentos
# quedarían "invisibles" (mezclados como texto suelto dentro del comando
# anterior) y corromperían el parseo en vez de ser detectados como no
# soportados por `_is_simplifiable`.
_COMMAND_SPLIT_RE = re.compile(r"([MLHVCSQTAZmlhvcsqtaz])")
_COORD_RE = re.compile(r"[-+]?\d*\.?\d+(?:[eE][-+]?\d+)?")

# Comandos que esta simplificación sabe interpretar y reescribir con
# seguridad: EXCLUSIVAMENTE M/L/Z absolutos en mayúscula. Cualquier otro
# comando presente en un <path> -- curvas ("C","S","Q","T"), arcos ("A"),
# líneas horizontales/verticales ("H","V"), o CUALQUIER variante relativa en
# minúscula (incluyendo "m","l","z") -- hace que ese <path> completo se deje
# intacto. Las minúsculas se excluyen deliberadamente: este pipeline no
# implementa acumulación de offsets relativos, así que tratarlas como
# soportadas produciría coordenadas erróneas en vez de una simplificación
# correcta -- ver limitación documentada arriba.
_SUPPORTED_COMMANDS = {"M", "L", "Z"}


def _local_name(tag: str) -> str:
    # Duplicado deliberado de app.core.svg_processing._local_name (privado):
    # mismo criterio de independencia entre módulos que
    # ThresholdingService/PreprocessingService (ver comentario en
    # threshold_service.py, "_reject_if_header_dimensions_exceed_limits").
    return tag.rsplit("}", 1)[-1].lower()


def _tokenize(d: str) -> list[tuple[str, list[float]]]:
    tokens = _COMMAND_SPLIT_RE.split(d)
    commands: list[tuple[str, list[float]]] = []
    index = 1
    while index < len(tokens):
        command = tokens[index]
        args_text = tokens[index + 1] if index + 1 < len(tokens) else ""
        numbers = [float(n) for n in _COORD_RE.findall(args_text)]
        commands.append((command, numbers))
        index += 2
    return commands


def _is_simplifiable(commands: list[tuple[str, list[float]]]) -> bool:
    # Comparación estricta (sin `.upper()`): los comandos relativos en
    # minúscula ("m","l","z") NO son soportados -- ver comentario de
    # _SUPPORTED_COMMANDS.
    return len(commands) > 0 and all(command in _SUPPORTED_COMMANDS for command, _ in commands)


def _extract_subpaths(commands: list[tuple[str, list[float]]]) -> list[dict]:
    """Agrupa los comandos ya tokenizados en subpaths (cada `M` empieza uno
    nuevo); soporta el caso de un único `<path d="...">` con múltiples
    subpaths (agujeros/hierarchical="stacked" de VTracer, o "texto trazado"
    con varios glifos/subpaths pequeños dentro del mismo `d`)."""
    subpaths: list[dict] = []
    current_points: list[Point] = []
    current_closed = False

    def _flush() -> None:
        if current_points:
            subpaths.append({"points": current_points, "closed": current_closed})

    for command, numbers in commands:
        upper = command.upper()
        if upper == "M":
            _flush()
            current_points = [(numbers[i], numbers[i + 1]) for i in range(0, len(numbers) - 1, 2)]
            current_closed = False
        elif upper == "L":
            current_points = current_points + [(numbers[i], numbers[i + 1]) for i in range(0, len(numbers) - 1, 2)]
        elif upper == "Z":
            current_closed = True

    _flush()
    return subpaths


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


def _perpendicular_distance(point: Point, start: Point, end: Point) -> float:
    x0, y0 = point
    x1, y1 = start
    x2, y2 = end
    dx, dy = x2 - x1, y2 - y1

    if dx == 0 and dy == 0:
        # Segmento degenerado (start == end, ej. el "cierre" artificial de un
        # subpath cerrado -- ver _simplify_subpath): distancia punto a punto.
        return math.hypot(x0 - x1, y0 - y1)

    numerator = abs(dy * x0 - dx * y0 + x2 * y1 - y2 * x1)
    denominator = math.hypot(dx, dy)
    return numerator / denominator


def _douglas_peucker(points: list[Point], epsilon: float) -> list[Point]:
    """Implementación iterativa (pila explícita, no recursión) de
    Douglas-Peucker: evita depender del límite de recursión de Python para
    polilíneas con muchos puntos (SVGs grandes/complejos)."""
    if len(points) < 3:
        return list(points)

    keep = [False] * len(points)
    keep[0] = True
    keep[-1] = True

    stack = [(0, len(points) - 1)]
    while stack:
        start_index, end_index = stack.pop()
        if end_index <= start_index + 1:
            continue

        start, end = points[start_index], points[end_index]
        max_distance = -1.0
        max_index = start_index

        for i in range(start_index + 1, end_index):
            distance = _perpendicular_distance(points[i], start, end)
            if distance > max_distance:
                max_distance = distance
                max_index = i

        if max_distance > epsilon:
            keep[max_index] = True
            stack.append((start_index, max_index))
            stack.append((max_index, end_index))

    return [point for point, kept in zip(points, keep) if kept]


def _simplify_subpath(points: list[Point], closed: bool, epsilon: float) -> list[Point]:
    if len(points) < 3:
        # Ya es lo más simple posible (un segmento o un punto): nada que
        # reducir, y forzar una simplificación acá solo podría corromperlo.
        return points

    if closed:
        # Un subpath cerrado no tiene "inicio"/"fin" naturales para
        # Douglas-Peucker clásico (que ancla los extremos del segmento). Se
        # aplica la técnica estándar de "abrir" el anillo repitiendo el primer
        # punto al final (el mismo punto sirve de ancla de inicio y fin), y se
        # descarta ese punto repetido del resultado -- el `Z` final vuelve a
        # cerrar la figura automáticamente sin necesitar la coordenada
        # duplicada. Esto preserva el cierre incluso con tolerancias grandes:
        # ver la salvaguarda de tamaño mínimo más abajo.
        ring = points + [points[0]]
        simplified_ring = _douglas_peucker(ring, epsilon)
        simplified = simplified_ring[:-1]

        if len(simplified) < 3:
            # Tolerancia extrema: colapsaría el polígono a una línea/punto, lo
            # que ya no sería un path cerrado válido. Se prioriza "no romper
            # paths válidos" (Definition of Done de spec.md) por sobre
            # simplificar agresivamente este subpath puntual -- se lo deja
            # sin cambios.
            return points

        return simplified

    simplified = _douglas_peucker(points, epsilon)
    if len(simplified) < 2:
        return points

    return simplified


def simplify_svg_paths(sanitized_svg: str, epsilon_ratio: float) -> str:
    """Punto de entrada: recibe un SVG YA sanitizado (ver
    app.core.svg_processing.sanitize_svg) y un epsilon relativo (fracción de
    la diagonal del bounding box del SVG completo, no un valor absoluto en
    píxeles -- así la tolerancia escala con el tamaño del diseño, ver
    Vectify.Api.Simplification.SimplificationOptions del lado .NET, que
    resuelve los presets Bajo/Medio/Alto a este valor numérico antes de
    llamar acá). Devuelve un nuevo string de SVG con los `d` de cada `<path>`
    simplificados; cualquier otro atributo/elemento (fill-rule, transform,
    orden de subpaths) queda intacto.
    """
    stats = compute_svg_stats(sanitized_svg)
    bounds = stats["bounds"]
    diagonal = math.hypot(bounds["width"], bounds["height"])
    if diagonal <= 0:
        # SVG vacío o degenerado (sin paths, o un único punto): nada que
        # simplificar de forma significativa: se devuelve intacto.
        return sanitized_svg

    epsilon = epsilon_ratio * diagonal

    root = ET.fromstring(sanitized_svg)
    for element in root.iter():
        if _local_name(element.tag) != "path":
            continue

        d = element.attrib.get("d", "")
        commands = _tokenize(d)
        if not _is_simplifiable(commands):
            # Comandos no soportados (ej. curvas "C", "H"/"V", o cualquier
            # variante en minúscula): se deja el path intacto -- ver
            # limitación documentada en el docstring del módulo.
            continue

        subpaths = _extract_subpaths(commands)
        rebuilt = "".join(
            _format_subpath(_simplify_subpath(subpath["points"], subpath["closed"], epsilon), subpath["closed"])
            for subpath in subpaths
        )
        if rebuilt:
            element.attrib["d"] = rebuilt

    return ET.tostring(root, encoding="unicode")
