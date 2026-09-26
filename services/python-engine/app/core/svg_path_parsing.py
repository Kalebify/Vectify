"""Utilidades de parseo de comandos de path SVG compartidas entre
app.core.simplification_pipeline (M1-S07) y app.core.path_checker (M1-S08):
tokenización de un atributo `d`, agrupamiento en subpaths, y el criterio
ÚNICO de "comando soportado" que ambos módulos deben respetar exactamente
igual -- una sola fuente de verdad, en vez de mantener dos copias del mismo
tokenizer que podrían divergir con el tiempo (como pasó una vez dentro de
app.core.simplification_pipeline dentro de M1-S07: ver "Fix post-review: dos
bugs en detección de comandos no soportados" en el reporte de ese sprint --
un regex de tokenización incompleto y una comparación de comandos no
estricta corrompieron paths en silencio en vez de dejarlos intactos).
Extraído a este módulo propio en M1-S08 para que el nuevo Laser Checker de
paths reutilice EXACTAMENTE la misma tokenización ya corregida y testeada,
en vez de volver a implementarla (y arriesgar reintroducir los mismos bugs).

Alcance/limitación (heredada por TODO módulo que use esto): SOLO se
interpretan con seguridad comandos M/L/Z absolutos en mayúscula -- el motor
de trazado actual (app.core.vector_engine.VtracerEngine, mode="polygon")
únicamente emite esa forma. Cualquier otro comando (curvas `C/S/Q/T`, arcos
`A`, líneas horizontales/verticales `H/V`, o CUALQUIER variante relativa en
minúscula, incluyendo `m/l/z`) hace que `is_supported_path` devuelva False
para ESE `<path>` completo -- los callers deben excluir ese `<path>` del
análisis por completo en vez de arriesgar interpretar semántica que no está
garantizada (ningún offset relativo se acumula acá, así que tratar una
variante relativa como si fuera absoluta produciría coordenadas erróneas).

Funciones puras y deterministas: mismo `d` -> mismo resultado, sin tocar
disco ni red (mismo criterio que app.core.svg_processing).
"""

import re
import xml.etree.ElementTree as ET

Point = tuple[float, float]

# Reconoce TODAS las letras de comando de path SVG (mayúsculas y
# minúsculas: M,L,C,Z,H,V,S,Q,T,A) como límites de token, no solo las
# soportadas -- si el regex no reconociera una letra de comando como
# límite, sus argumentos quedarían "invisibles" (mezclados como texto
# suelto dentro del comando anterior) y corromperían el parseo en vez de
# detectarse como no soportados por `is_supported_path`.
_COMMAND_SPLIT_RE = re.compile(r"([MLHVCSQTAZmlhvcsqtaz])")
_COORD_RE = re.compile(r"[-+]?\d*\.?\d+(?:[eE][-+]?\d+)?")

# Reconoce EXACTAMENTE `translate(tx,ty)` o `translate(tx)` (equivalente a
# ty=0, forma válida en SVG) y NADA MÁS -- ni transforms encadenados, ni
# otras funciones (rotate/scale/matrix/skew). `fullmatch` sobre el valor ya
# recortado de espacios: cualquier cosa que no calce por completo se trata
# como "no soportado" en vez de intentar interpretarla parcialmente. Idéntico
# al regex privado de app.core.path_checker (M1-S08) -- se duplica acá
# deliberadamente en vez de importarlo (símbolo privado de otro módulo) para
# no crear un acoplamiento entre M1-S08 y esta infraestructura compartida;
# ambos deben seguir aceptando EXACTAMENTE el mismo patrón si alguna vez se
# amplía.
_TRANSLATE_ONLY_RE = re.compile(
    r"translate\(\s*([-+]?\d*\.?\d+(?:[eE][-+]?\d+)?)"
    r"(?:[\s,]+([-+]?\d*\.?\d+(?:[eE][-+]?\d+)?))?\s*\)"
)

# Comandos que se saben interpretar con seguridad: EXCLUSIVAMENTE M/L/Z
# absolutos en mayúscula. Las minúsculas se excluyen deliberadamente:
# ningún caller de este módulo acumula offsets relativos, así que tratarlas
# como soportadas produciría coordenadas erróneas en vez de una
# interpretación correcta.
SUPPORTED_COMMANDS = {"M", "L", "Z"}


def local_name(tag: str) -> str:
    """Nombre de elemento/atributo sin el namespace XML (ej. "path")."""
    return tag.rsplit("}", 1)[-1].lower()


def tokenize_path_d(d: str) -> list[tuple[str, list[float]]]:
    """Divide el atributo `d` de un `<path>` en pares (comando, [coords]),
    en el orden en que aparecen. No interpreta semántica (no sabe qué
    comandos son "soportados") -- eso lo decide `is_supported_path`."""
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


def is_supported_path(commands: list[tuple[str, list[float]]]) -> bool:
    """True si TODOS los comandos ya tokenizados de un `<path>` son M/L/Z
    absolutos en mayúscula (comparación estricta, sin `.upper()`: las
    minúsculas relativas NO son soportadas -- ver SUPPORTED_COMMANDS) y hay
    al menos un comando. Un único comando no soportado invalida el `<path>`
    completo: no hay una forma segura de "analizar parcialmente" un path
    cuya semántica de algunos de sus comandos no se entiende."""
    return len(commands) > 0 and all(command in SUPPORTED_COMMANDS for command, _ in commands)


def extract_subpaths(commands: list[tuple[str, list[float]]]) -> list[dict]:
    """Agrupa comandos YA tokenizados en subpaths (cada `M` empieza uno
    nuevo); soporta múltiples subpaths dentro de un único `<path d="...">`
    (agujeros/hierarchical="stacked" de VTracer, o varios glifos de "texto
    trazado"). Cada subpath es `{"points": [...], "closed": bool}` -- solo
    tiene sentido llamar esto sobre comandos ya confirmados como
    `is_supported_path`; con comandos no soportados el resultado no sería
    fiable (H/V/C/etc. no aportan puntos a `numbers` de la forma esperada).
    """
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


def resolve_translate_transform(transform: str) -> tuple[float, float] | None:
    """Resuelve el atributo `transform` de un `<path>` a un offset `(tx, ty)`
    a aplicar sobre sus puntos LOCALES para obtener coordenadas ABSOLUTAS del
    lienzo (mismo criterio que app.core.path_checker, fix post-M1-S08: el `d`
    de un `<path>` está en coordenadas LOCALES a ese `<path>`, VtracerEngine
    puede emitir formas congruentes en posiciones reales distintas del
    lienzo repitiendo el MISMO `d` local con su propio
    `transform="translate(tx,ty)"`). Devuelve `(0.0, 0.0)` si el atributo
    está ausente o vacío (ningún transform). Devuelve `None` si el valor NO
    es exactamente `translate(tx,ty)` o `translate(tx)` (ver
    `_TRANSLATE_ONLY_RE`) -- el caller debe tratar eso como "no soportado" y
    excluir el `<path>` completo, con el mismo criterio que un comando de
    path no soportado (nunca se ignora un transform en silencio)."""
    stripped = transform.strip()
    if not stripped:
        return 0.0, 0.0

    match = _TRANSLATE_ONLY_RE.fullmatch(stripped)
    if not match:
        return None

    tx = float(match.group(1))
    ty = float(match.group(2)) if match.group(2) is not None else 0.0
    return tx, ty


def collect_document_subpaths(root: ET.Element) -> tuple[list[dict], int]:
    """Recorre un documento SVG YA parseado (raíz `<svg>`) en orden y
    devuelve la lista de subpaths analizables -- de `<path>` con comandos
    soportados Y `transform` resoluble, ver `is_supported_path`/
    `resolve_translate_transform` -- junto con la cantidad de `<path>`
    excluidos (`skipped_path_count`). Generalización pública, compartida, de
    la recolección que introdujo app.core.path_checker (M1-S08) para
    resolver duplicados/paths abiertos; se agrega acá (en el módulo de
    tokenización YA compartido) para que cualquier análisis geométrico
    nuevo sobre subpaths en coordenadas ABSOLUTAS (ej. componentes físicos
    independientes, M2-S03) reutilice EXACTAMENTE el mismo criterio de
    "qué subpath es analizable" en vez de mantener una tercera copia.

    Cada subpath devuelto lleva `path_index` (índice del `<path>` en el
    documento, 0-based, CONTANDO también los excluidos -- así el índice
    coincide con el orden real de `<path>` del SVG que renderiza React),
    `subpath_index` (índice del subpath dentro de ese `<path>`, 0-based),
    `points` (ya en coordenadas ABSOLUTAS del lienzo, con el offset de
    `transform` aplicado), `closed` y `bounds`. Subpaths de menos de 2
    puntos (un único `M` sin `L` posteriores -- sin ningún segmento) se
    excluyen: no hay geometría que analizar."""
    subpaths: list[dict] = []
    skipped_path_count = 0
    path_index = 0

    for element in root.iter():
        if local_name(element.tag) != "path":
            continue

        d = element.attrib.get("d", "")
        commands = tokenize_path_d(d)
        offset = resolve_translate_transform(element.attrib.get("transform", ""))

        if not is_supported_path(commands) or offset is None:
            skipped_path_count += 1
            path_index += 1
            continue

        tx, ty = offset

        for subpath_index, subpath in enumerate(extract_subpaths(commands)):
            points = subpath["points"]
            if len(points) < 2:
                continue
            absolute_points = [(x + tx, y + ty) for x, y in points]
            xs = [p[0] for p in absolute_points]
            ys = [p[1] for p in absolute_points]
            subpaths.append(
                {
                    "path_index": path_index,
                    "subpath_index": subpath_index,
                    "points": absolute_points,
                    "closed": subpath["closed"],
                    "bounds": {"min_x": min(xs), "min_y": min(ys), "max_x": max(xs), "max_y": max(ys)},
                }
            )

        path_index += 1

    return subpaths, skipped_path_count
