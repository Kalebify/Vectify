"""Servicio que orquesta la unión física de piezas (M2-S06): recibe un SVG
YA generado por vectorización (una capa de un solo color, M2-S02) -- lo
re-sanitiza/revalida defensivamente (nunca confía ciegamente en que el
caller, Vectify.Api, ya lo hizo -- mismo criterio que
ComponentAnalysisService/PathCheckerService), corre la unión geométrica de
app.core.physical_union (booleanas + bridging simple, con la validación
post-operación no negociable de "nunca fingir unión" YA aplicada adentro de
esa función) y devuelve el SVG resultante tipado. A diferencia de
ComponentAnalysisService (solo lectura), este servicio SÍ modifica
geometría -- pero nunca escribe a disco ni conoce el concepto de
"VectorVersion": eso es responsabilidad exclusiva de Vectify.Api
(PhysicalUnionService.cs), que decide si persiste el resultado como una
VectorVersion nueva (solo al "confirmar", nunca en un "preview").
"""

import concurrent.futures
import xml.etree.ElementTree as ET
from typing import Callable

from app.core.config import Settings
from app.core.errors import InvalidInputSvgError, InvalidSvgError, PhysicalUnionTimeoutError, SvgInputTooLargeError
from app.core.physical_union import union_selected_components
from app.core.svg_path_parsing import local_name
from app.core.svg_processing import compute_svg_stats, sanitize_svg
from app.models.schemas import PhysicalUnionParams, PhysicalUnionResponse, VectorBounds, VectorMetrics

UnionFn = Callable[[str, list[dict], float, float, float, int], dict]


class PhysicalUnionService:
    def __init__(self, settings: Settings, union_fn: UnionFn | None = None) -> None:
        self._settings = settings
        # `union_fn` inyectable -- mismo criterio que `analyze_fn` de
        # ComponentAnalysisService, para poder simular una unión lenta en
        # tests de timeout sin depender de que un SVG real sea lo bastante
        # grande/complejo como para tardar.
        self._union_fn = union_fn or union_selected_components

    def process(self, svg_bytes: bytes, params: PhysicalUnionParams) -> PhysicalUnionResponse:
        """Une los componentes indicados en `params.selections` sobre el SVG
        recibido. Determinista: mismos `svg_bytes` + mismos `params` siempre
        producen el mismo resultado (o el mismo error), en el mismo orden.
        """
        if len(svg_bytes) > self._settings.max_svg_output_bytes:
            raise SvgInputTooLargeError(
                f"El SVG de entrada ({len(svg_bytes)} bytes) supera el límite permitido "
                f"({self._settings.max_svg_output_bytes} bytes)."
            )

        svg_text = self._decode(svg_bytes)

        try:
            sanitized_input = sanitize_svg(svg_text)
        except InvalidSvgError as exc:
            raise InvalidInputSvgError(str(exc)) from exc

        selections = [
            {
                "component_id": selection.component_id,
                "members": [
                    {"path_index": member.path_index, "subpath_index": member.subpath_index, "role": member.role}
                    for member in selection.members
                ],
            }
            for selection in params.selections
        ]

        result = self._union_with_timeout(
            sanitized_input, selections, params.touch_ratio, params.tiny_area_ratio, params.bridge_width_ratio
        )

        merged_svg = result["svg"]
        width, height = _read_canvas_size(merged_svg)
        stats = compute_svg_stats(merged_svg)
        bounds = stats["bounds"]

        return PhysicalUnionResponse(
            svg=merged_svg,
            width=width,
            height=height,
            metrics=VectorMetrics(
                path_count=stats["path_count"],
                approx_node_count=stats["approx_node_count"],
                bounds=VectorBounds(**bounds),
            ),
            effective_params=params,
            component_count_before=result["component_count_before"],
            component_count_after=result["component_count_after"],
            expected_component_count_after=result["expected_component_count_after"],
            strategy=result["strategy"],
            bridge_count=result["bridge_count"],
        )

    def _decode(self, svg_bytes: bytes) -> str:
        try:
            return svg_bytes.decode("utf-8")
        except UnicodeDecodeError as exc:
            raise InvalidInputSvgError(f"El SVG de entrada no es UTF-8 válido: {exc}") from exc

    def _union_with_timeout(
        self,
        svg_text: str,
        selections: list[dict],
        touch_ratio: float,
        tiny_area_ratio: float,
        bridge_width_ratio: float,
    ) -> dict:
        """Aplica PhysicalUnion:TimeoutSeconds (physical_union_timeout_seconds).
        Mismo criterio (y misma corrección de bug conocida del proyecto) que
        ComponentAnalysisService._analyze_with_timeout: el cómputo
        booleano/bridging + la doble pasada de análisis de componentes es
        puro Python/GEOS sin punto de cancelación cooperativa, así que el
        timeout se implementa ejecutándolo en un hilo aparte y
        abandonándolo si no responde a tiempo (nunca `with
        ThreadPoolExecutor(...)`: su `__exit__` bloquearía este método hasta
        que el hilo colgado termine, anulando el propósito del timeout).
        """
        executor = concurrent.futures.ThreadPoolExecutor(max_workers=1)
        future = executor.submit(
            self._union_fn,
            svg_text,
            selections,
            touch_ratio,
            tiny_area_ratio,
            bridge_width_ratio,
            self._settings.max_component_subpaths,
        )
        try:
            result = future.result(timeout=self._settings.physical_union_timeout_seconds)
        except concurrent.futures.TimeoutError as exc:
            executor.shutdown(wait=False)
            raise PhysicalUnionTimeoutError(
                f"La unión física tardó más de {self._settings.physical_union_timeout_seconds}s y se abortó."
            ) from exc
        executor.shutdown(wait=True)
        return result


def _read_canvas_size(svg_text: str) -> tuple[int, int]:
    """Lee `width`/`height` del elemento `<svg>` raíz (ya sanitizado): la
    unión física nunca cambia el tamaño del lienzo, solo su contenido, así
    que estos valores vienen del documento en sí -- a diferencia de
    VectorizeResponse (M1-S05), donde width/height vienen de la imagen
    raster de origen, acá no existe tal cosa (el "origen" ya es un SVG)."""
    root = ET.fromstring(svg_text)
    if local_name(root.tag) != "svg":
        return 0, 0
    try:
        return int(round(float(root.attrib.get("width", "0")))), int(round(float(root.attrib.get("height", "0"))))
    except ValueError:
        return 0, 0
