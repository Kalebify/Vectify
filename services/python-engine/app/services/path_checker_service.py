"""Servicio que orquesta el Laser Checker de paths abiertos/duplicados
(M1-S08): recibe un SVG YA generado por vectorización (M1-S05) o por una
simplificación (M1-S07), lo re-sanitiza/revalida defensivamente (nunca
confía ciegamente en que el caller, Vectify.Api, ya lo hizo -- mismo
criterio que SimplificationService/ThresholdingService/VectorizationService),
corre el análisis geométrico de app.core.path_checker con una tolerancia
relativa al tamaño del diseño, y devuelve los issues tipados. A diferencia de
todas las etapas anteriores del pipeline, este servicio NUNCA modifica el SVG
ni produce uno nuevo -- es puramente de lectura/diagnóstico (ver spec.md
M1-S08, Definition of Done: "sin modificar el SVG").
"""

import concurrent.futures
from typing import Callable

from app.core.config import Settings
from app.core.errors import CheckTimeoutError, InvalidInputSvgError, InvalidSvgError, SvgInputTooLargeError
from app.core.path_checker import analyze_svg_paths
from app.core.svg_processing import sanitize_svg
from app.models.schemas import (
    CheckBounds,
    CheckParams,
    CheckResponse,
    CheckSummary,
    DuplicateMember,
    DuplicatePathIssue,
    OpenPathIssue,
)

CheckFn = Callable[[str, float, float, int], dict]


class PathCheckerService:
    def __init__(self, settings: Settings, check_fn: CheckFn | None = None) -> None:
        self._settings = settings
        # `check_fn` es inyectable (mismo criterio que `simplify_fn` en
        # SimplificationService) para poder simular un análisis lento en
        # tests de timeout sin depender de que un SVG real sea lo bastante
        # grande como para tardar -- ver tests/test_path_checker_service.py.
        self._check_fn = check_fn or analyze_svg_paths

    def process(self, svg_bytes: bytes, params: CheckParams) -> CheckResponse:
        """Analiza los bytes recibidos (un SVG ya vectorizado o simplificado
        -- nunca una imagen raster) con los parámetros ya validados.
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
            # de INPUT del caller (400) -- mismo criterio que
            # SimplificationService.
            raise InvalidInputSvgError(str(exc)) from exc

        result = self._check_with_timeout(
            sanitized_input, params.close_gap_ratio, params.duplicate_point_ratio
        )

        open_path_issues = [OpenPathIssue(**_open_path_payload(issue)) for issue in result["open_path_issues"]]
        duplicate_issues = [
            DuplicatePathIssue(**_duplicate_payload(issue)) for issue in result["duplicate_issues"]
        ]

        return CheckResponse(
            effective_params=params,
            summary=CheckSummary(
                open_path_count=len(open_path_issues),
                duplicate_group_count=len(duplicate_issues),
            ),
            # Orden reproducible y estable: primero los paths abiertos (ya en
            # orden de documento), después los grupos de duplicados (ya en
            # orden de primer miembro) -- ver app.core.path_checker.
            issues=[*open_path_issues, *duplicate_issues],
            skipped_path_count=result["skipped_path_count"],
        )

    def _decode(self, svg_bytes: bytes) -> str:
        try:
            return svg_bytes.decode("utf-8")
        except UnicodeDecodeError as exc:
            raise InvalidInputSvgError(f"El SVG de entrada no es UTF-8 válido: {exc}") from exc

    def _check_with_timeout(self, svg_text: str, close_gap_ratio: float, duplicate_point_ratio: float) -> dict:
        """Aplica Check:TimeoutSeconds (check_timeout_seconds) al análisis.
        Mismo criterio (y misma corrección de bug) que
        SimplificationService._simplify_with_timeout: la detección de
        duplicados es O(n^2) en Python puro sin un punto de cancelación
        cooperativa dentro del bucle, así que el timeout se implementa
        ejecutándola en un hilo aparte y abandonándolo si no responde a
        tiempo. Deliberadamente NO se usa `with ThreadPoolExecutor(...)`: su
        `__exit__` llama a `shutdown(wait=True)`, que bloquearía este método
        (y a su caller) hasta que el hilo colgado termine, anulando el
        propósito del timeout.
        """
        executor = concurrent.futures.ThreadPoolExecutor(max_workers=1)
        future = executor.submit(
            self._check_fn,
            svg_text,
            close_gap_ratio,
            duplicate_point_ratio,
            self._settings.max_check_subpaths,
        )
        try:
            result = future.result(timeout=self._settings.check_timeout_seconds)
        except concurrent.futures.TimeoutError as exc:
            executor.shutdown(wait=False)
            raise CheckTimeoutError(
                f"El análisis de paths tardó más de {self._settings.check_timeout_seconds}s y se abortó."
            ) from exc
        executor.shutdown(wait=True)
        return result


def _open_path_payload(issue: dict) -> dict:
    payload = dict(issue)
    payload["bounds"] = CheckBounds(**payload["bounds"])
    return payload


def _duplicate_payload(issue: dict) -> dict:
    payload = dict(issue)
    payload["members"] = [
        DuplicateMember(path_index=member["path_index"], subpath_index=member["subpath_index"], bounds=CheckBounds(**member["bounds"]))
        for member in payload["members"]
    ]
    return payload
