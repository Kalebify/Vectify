"""Sanitización defensiva del SVG generado por el motor de vectorización
(app.core.vector_engine.VectorEngine) y cálculo de estadísticas (paths, nodos
aproximados, bounds) sobre el resultado YA sanitizado. Deliberadamente
agnóstico al motor: recibe/devuelve strings de SVG, nunca un tipo propio de
VTracer -- mismo criterio de encapsulamiento que vector_engine.py.

Funciones puras y deterministas: mismo string de entrada -> misma salida,
sin tocar disco ni red (mismo criterio que app.core.pipeline).

Ver spec.md M1-S05:
- "Seguridad/robustez": "Sanitizar SVG generado" -- sin <script>, sin
  referencias externas peligrosas (xlink:href/href a URLs externas),
  sin manejadores de eventos inline (onclick, onload, ...).
- "Python/FastAPI": "normalizar SVG; devolver estadísticas como
  paths/nodos aproximados/bounds".
"""

import re
import xml.etree.ElementTree as ET

from app.core.errors import InvalidSvgError

_SVG_NS = "http://www.w3.org/2000/svg"
ET.register_namespace("", _SVG_NS)

# Elementos que nunca deberían sobrevivir la sanitización, sin importar qué
# motor los haya generado: <script> (ejecución arbitraria), <foreignObject>
# (permite incrustar HTML/JS dentro de SVG), <iframe>/<embed>/<object>
# (carga de contenido externo), <audio>/<video> (carga de medios externos).
_DANGEROUS_LOCAL_NAMES = {"script", "foreignobject", "iframe", "embed", "object", "audio", "video"}

# Defensa contra XXE/expansión de entidades: se rechaza cualquier DOCTYPE o
# declaración de entidad ANTES de parsear, en vez de confiar en que el parser
# no las resuelva. VTracer (trazado geométrico puro) nunca debería emitir
# nada de esto -- si aparece, es una señal de que la entrada no es de fiar.
_DOCTYPE_OR_ENTITY_RE = re.compile(r"<!\s*(DOCTYPE|ENTITY)", re.IGNORECASE)

_COORD_RE = re.compile(r"[-+]?\d*\.?\d+(?:[eE][-+]?\d+)?")
_COMMAND_SPLIT_RE = re.compile(r"([MLCZmlcz])")
_TRANSLATE_RE = re.compile(r"translate\(\s*([-+]?\d*\.?\d+)\s*[, ]\s*([-+]?\d*\.?\d+)?\s*\)")


def _local_name(tag: str) -> str:
    return tag.rsplit("}", 1)[-1].lower()


def sanitize_svg(raw_svg: str) -> str:
    """Parsea el SVG crudo (falla con InvalidSvgError si no es XML válido o
    si el elemento raíz no es <svg>), elimina elementos/atributos peligrosos
    y devuelve el marcado re-serializado. El resultado siempre es XML válido
    por construcción (se re-serializa desde el árbol ya parseado) -- ver
    spec.md, criterio de aceptación "SVG resultante válido (parseable como
    XML/SVG)".
    """
    if _DOCTYPE_OR_ENTITY_RE.search(raw_svg):
        raise InvalidSvgError("El SVG generado contiene un DOCTYPE/ENTITY, rechazado por seguridad.")

    try:
        root = ET.fromstring(raw_svg)
    except ET.ParseError as exc:
        raise InvalidSvgError(f"El SVG generado no es XML válido: {exc}") from exc

    if _local_name(root.tag) != "svg":
        raise InvalidSvgError("El contenido generado no tiene un elemento <svg> como raíz.")

    _strip_dangerous_elements(root)
    _strip_dangerous_attributes(root)

    return ET.tostring(root, encoding="unicode")


def _strip_dangerous_elements(root: ET.Element) -> None:
    for parent in root.iter():
        for child in list(parent):
            if _local_name(child.tag) in _DANGEROUS_LOCAL_NAMES:
                parent.remove(child)


def _strip_dangerous_attributes(root: ET.Element) -> None:
    for element in root.iter():
        for attr_name in list(element.attrib):
            local = _local_name(attr_name)
            value = element.attrib[attr_name]

            # Manejadores de eventos inline (onload, onclick, onmouseover, ...).
            if local.startswith("on"):
                del element.attrib[attr_name]
                continue

            # href / xlink:href: solo se permiten referencias internas
            # (fragmentos "#id", usados por ej. para <use>/gradientes dentro
            # del mismo documento). Cualquier otra cosa (http(s)://, //,
            # data:, javascript:, rutas relativas) se elimina -- spec.md pide
            # explícitamente "sin referencias externas".
            if local == "href" and not value.startswith("#"):
                del element.attrib[attr_name]
                continue

            # Defensa adicional: cualquier atributo (ej. `style`, con
            # `url(javascript:...)`) que contenga el esquema javascript: se
            # elimina, sin importar su nombre.
            if "javascript:" in value.lower():
                del element.attrib[attr_name]


def compute_svg_stats(sanitized_svg: str) -> dict:
    """Calcula paths/nodos aproximados/bounds sobre un SVG YA sanitizado
    (se asume bien formado -- llamar después de `sanitize_svg`). Solo
    entiende los comandos que emite VtracerEngine con `mode="polygon"`
    (M/L/Z, coordenadas absolutas) y, de forma best-effort/conservadora,
    `C` (curvas), usando los puntos de control como cota externa segura del
    bounding box real -- documentado como aproximación en el reporte del
    sprint. También aplica el offset de `transform="translate(tx,ty)"` de
    cada `<path>` (VTracer lo emite por grupo), si está presente.
    """
    root = ET.fromstring(sanitized_svg)
    paths = [el for el in root.iter() if _local_name(el.tag) == "path"]

    path_count = len(paths)
    approx_node_count = 0
    min_x = min_y = float("inf")
    max_x = max_y = float("-inf")

    for path in paths:
        d = path.attrib.get("d", "")
        tx, ty = _parse_translate(path.attrib.get("transform", ""))

        tokens = _COMMAND_SPLIT_RE.split(d)
        # tokens: [texto_antes_del_primer_comando, CMD, args, CMD, args, ...]
        index = 1
        while index < len(tokens):
            command = tokens[index].upper()
            args_text = tokens[index + 1] if index + 1 < len(tokens) else ""
            numbers = [float(n) for n in _COORD_RE.findall(args_text)]

            if command in ("M", "L"):
                for i in range(0, len(numbers) - 1, 2):
                    x, y = numbers[i] + tx, numbers[i + 1] + ty
                    min_x, max_x = min(min_x, x), max(max_x, x)
                    min_y, max_y = min(min_y, y), max(max_y, y)
                    approx_node_count += 1
            elif command == "C":
                for i in range(0, len(numbers) - 1, 2):
                    x, y = numbers[i] + tx, numbers[i + 1] + ty
                    min_x, max_x = min(min_x, x), max(max_x, x)
                    min_y, max_y = min(min_y, y), max(max_y, y)
                # Una curva C tiene 3 pares (2 de control + 1 punto final);
                # se cuenta como un único nodo (el punto final), aproximado.
                if len(numbers) >= 6:
                    approx_node_count += 1

            index += 2

    if path_count == 0 or min_x == float("inf"):
        bounds = {"min_x": 0.0, "min_y": 0.0, "max_x": 0.0, "max_y": 0.0, "width": 0.0, "height": 0.0}
    else:
        bounds = {
            "min_x": min_x,
            "min_y": min_y,
            "max_x": max_x,
            "max_y": max_y,
            "width": max_x - min_x,
            "height": max_y - min_y,
        }

    return {"path_count": path_count, "approx_node_count": approx_node_count, "bounds": bounds}


def _parse_translate(transform: str) -> tuple[float, float]:
    match = _TRANSLATE_RE.search(transform)
    if not match:
        return 0.0, 0.0

    tx = float(match.group(1))
    ty = float(match.group(2)) if match.group(2) is not None else 0.0
    return tx, ty
