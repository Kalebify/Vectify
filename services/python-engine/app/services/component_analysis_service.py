"""Servicio que orquesta el análisis de componentes físicos independientes
por capa (M2-S03): recibe un SVG YA generado por vectorización (M1-S05) --
una capa vectorial de un solo color (M2-S02) -- lo re-sanitiza/revalida
defensivamente (nunca confía ciegamente en que el caller, Vectify.Api, ya lo
hizo -- mismo criterio que PathCheckerService/SimplificationService), corre
el análisis geométrico de app.core.component_analysis con las tolerancias
efectivas, y devuelve los componentes tipados. Igual que PathCheckerService,
este servicio NUNCA modifica el SVG ni produce uno nuevo -- es puramente de
lectura/diagnóstico (ver spec.md M2-S03, "Fuera de alcance": "modificar la
geometría para unirlos ... eso sería M2-S06").
"""

import concurrent.futures
from typing import Callable

from app.core.component_analysis import analyze_svg_components
from app.core.config import Settings
from app.core.errors import ComponentAnalysisTimeoutError, InvalidInputSvgError, InvalidSvgError, SvgInputTooLargeError
from app.core.svg_processing import sanitize_svg
from app.models.schemas import (
    ComponentAnalysisParams,
    ComponentAnalysisResponse,
    ComponentBounds,
    ComponentItem,
    ComponentMember,
    ComponentSummary,
)

AnalyzeFn = Callable[[str, float, float, int], dict]


class ComponentAnalysisService:
    def __init__(self, settings: Settings, analyze_fn: AnalyzeFn | None = None) -> None:
        self._settings = settings
        # `analyze_fn` es inyectable (mismo criterio que `check_fn` en
        # PathCheckerService) para poder simular un análisis lento en tests
        # de timeout sin depender de que un SVG real sea lo bastante grande
        # como para tardar -- ver tests/test_component_analysis_service.py.
        self._analyze_fn = analyze_fn or analyze_svg_components

    def process(self, svg_bytes: bytes, params: ComponentAnalysisParams) -> ComponentAnalysisResponse:
        """Analiza los bytes recibidos (un SVG ya vectorizado -- una capa de
        M2-S02 -- nunca una imagen raster) con los parámetros ya validados.
        Determinista: mismos `svg_bytes` + mismos `params` siempre producen
        el mismo resultado, en el mismo orden.
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
            # Traduce el error "interno" de sanitize_svg (pensado para el SVG
            # crudo recién generado por el motor de trazado, 500) a un error
            # de INPUT del caller (400) -- mismo criterio que PathCheckerService.
            raise InvalidInputSvgError(str(exc)) from exc

        result = self._analyze_with_timeout(sanitized_input, params.touch_ratio, params.tiny_area_ratio)

        components = [ComponentItem(**_component_payload(component)) for component in result["components"]]
        tiny_count = sum(1 for component in components if component.is_tiny)

        return ComponentAnalysisResponse(
            effective_params=params,
            summary=ComponentSummary(component_count=len(components), tiny_component_count=tiny_count),
            components=components,
            skipped_path_count=result["skipped_path_count"],
        )

    def _decode(self, svg_bytes: bytes) -> str:
        try:
            return svg_bytes.decode("utf-8")
        except UnicodeDecodeError as exc:
            raise InvalidInputSvgError(f"El SVG de entrada no es UTF-8 válido: {exc}") from exc

    def _analyze_with_timeout(self, svg_text: str, touch_ratio: float, tiny_area_ratio: float) -> dict:
        """Aplica Component:TimeoutSeconds (component_timeout_seconds) al
        análisis. Mismo criterio (y misma corrección de bug) que
        PathCheckerService._check_with_timeout: tanto la detección de
        contención como la de contacto son O(n^2) en Python puro sin un
        punto de cancelación cooperativa dentro del bucle, así que el
        timeout se implementa ejecutándolo en un hilo aparte y
        abandonándolo si no responde a tiempo. Deliberadamente NO se usa
        `with ThreadPoolExecutor(...)`: su `__exit__` llama a
        `shutdown(wait=True)`, que bloquearía este método (y a su caller)
        hasta que el hilo colgado termine, anulando el propósito del
        timeout.
        """
        executor = concurrent.futures.ThreadPoolExecutor(max_workers=1)
        future = executor.submit(
            self._analyze_fn,
            svg_text,
            touch_ratio,
            tiny_area_ratio,
            self._settings.max_component_subpaths,
        )
        try:
            result = future.result(timeout=self._settings.component_timeout_seconds)
        except concurrent.futures.TimeoutError as exc:
            executor.shutdown(wait=False)
            raise ComponentAnalysisTimeoutError(
                f"El análisis de componentes tardó más de {self._settings.component_timeout_seconds}s y se abortó."
            ) from exc
        executor.shutdown(wait=True)
        return result


def _component_payload(component: dict) -> dict:
    payload = dict(component)
    payload["bounds"] = ComponentBounds(**payload["bounds"])
    payload["members"] = [
        ComponentMember(
            path_index=member["path_index"],
            subpath_index=member["subpath_index"],
            role=member["role"],
            bounds=ComponentBounds(**member["bounds"]),
            area=member["area"],
        )
        for member in payload["members"]
    ]
    return payload
