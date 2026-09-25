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

Point = tuple[float, float]

# Reconoce TODAS las letras de comando de path SVG (mayúsculas y
# minúsculas: M,L,C,Z,H,V,S,Q,T,A) como límites de token, no solo las
# soportadas -- si el regex no reconociera una letra de comando como
# límite, sus argumentos quedarían "invisibles" (mezclados como texto
# suelto dentro del comando anterior) y corromperían el parseo en vez de
# detectarse como no soportados por `is_supported_path`.
_COMMAND_SPLIT_RE = re.compile(r"([MLHVCSQTAZmlhvcsqtaz])")
_COORD_RE = re.compile(r"[-+]?\d*\.?\d+(?:[eE][-+]?\d+)?")

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
