"""Servicio que orquesta la etapa de simplificación de nodos (M1-S07): recibe
un SVG YA generado por vectorización (o por una simplificación previa -- el
usuario puede volver a simplificar sobre un resultado ya simplificado, mismo
criterio de "cadena de versiones" que el resto del pipeline), lo re-sanitiza/
revalida defensivamente (nunca confía ciegamente en que el caller, Vectify.Api,
ya lo hizo -- mismo criterio que ThresholdingService/VectorizationService), le
reduce la cantidad de nodos a cada `<path>` con Douglas-Peucker (ver
app.core.simplification_pipeline) con una tolerancia relativa al tamaño del
diseño, y devuelve el SVG resultante junto con nodeCount antes/después y % de
reducción. Separado de VectorizationService por ser una etapa distinta y
POSTERIOR del pipeline (opera sobre un SVG ya vectorizado, nunca sobre la
máscara B/N ni el original) -- ver spec.md M1-S07, y la convención ya
establecida de un módulo por etapa (Preprocessing/Threshold/Vectorization).
"""

import concurrent.futures
from typing import Callable

from app.core.config import Settings
from app.core.errors import InvalidInputSvgError, InvalidSvgError, SimplificationTimeoutError, SvgInputTooLargeError
from app.core.simplification_pipeline import simplify_svg_paths
from app.core.svg_processing import compute_svg_stats, sanitize_svg
from app.models.schemas import SimplifyMetrics, SimplifyParams, SimplifyResponse, VectorMetrics

SimplifyFn = Callable[[str, float], str]


class SimplificationService:
    def __init__(self, settings: Settings, simplify_fn: SimplifyFn | None = None) -> None:
        self._settings = settings
        # `simplify_fn` es inyectable (mismo criterio que VectorEngine en
        # VectorizationService) para poder simular un motor lento en tests de
        # timeout sin depender de que Douglas-Peucker real tarde -- ver
        # tests/test_simplification_service.py.
        self._simplify_fn = simplify_fn or simplify_svg_paths

    def process(self, svg_bytes: bytes, params: SimplifyParams) -> SimplifyResponse:
        """Genera un SVG con menos nodos a partir de los bytes recibidos (un
        SVG ya vectorizado -- nunca una imagen raster) y los parámetros ya
        validados. Determinista: mismos `svg_bytes` + mismos `params` siempre
        producen el mismo resultado.
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
            # de INPUT del caller (400) -- ver InvalidInputSvgError.
            raise InvalidInputSvgError(str(exc)) from exc

        before_stats = compute_svg_stats(sanitized_input)

        simplified_svg = self._simplify_with_timeout(sanitized_input, params.epsilon_ratio)

        try:
            sanitized_output = sanitize_svg(simplified_svg)
        except InvalidSvgError as exc:
            # Esto sí sería un fallo interno de la propia simplificación (bug
            # en app.core.simplification_pipeline produciendo XML inválido),
            # no un problema del input -- se deja como InvalidSvgError (500),
            # mismo criterio que VectorizationService con el SVG crudo del motor.
            raise InvalidSvgError(
                f"La simplificación produjo un SVG inválido: {exc}"
            ) from exc

        after_stats = compute_svg_stats(sanitized_output)
        reduction = _reduction_percent(before_stats["approx_node_count"], after_stats["approx_node_count"])

        return SimplifyResponse(
            svg=sanitized_output,
            content_type="image/svg+xml",
            effective_params=params,
            metrics=SimplifyMetrics(
                before=VectorMetrics(**before_stats),
                after=VectorMetrics(**after_stats),
                reduction_percent=reduction,
            ),
        )

    def _decode(self, svg_bytes: bytes) -> str:
        try:
            return svg_bytes.decode("utf-8")
        except UnicodeDecodeError as exc:
            raise InvalidInputSvgError(f"El SVG de entrada no es UTF-8 válido: {exc}") from exc

    def _simplify_with_timeout(self, svg_text: str, epsilon_ratio: float) -> str:
        """Aplica Simplify:TimeoutSeconds (simplify_timeout_seconds) a la
        simplificación. Mismo criterio (y misma corrección de bug) que
        VectorizationService._trace_with_timeout: Douglas-Peucker es Python
        puro sin un punto de cancelación cooperativa dentro del bucle, así
        que el timeout se implementa ejecutándolo en un hilo aparte y
        abandonándolo si no responde a tiempo. Deliberadamente NO se usa
        `with ThreadPoolExecutor(...)`: su `__exit__` llama a
        `shutdown(wait=True)`, que bloquearía este método (y a su caller)
        hasta que el hilo colgado termine, anulando el propósito del timeout.
        En el camino de timeout se hace `shutdown(wait=False)` para retornar
        sin esperar al hilo; en el camino feliz `future.result()` ya implica
        que el hilo terminó, así que el shutdown no bloquea.
        """
        executor = concurrent.futures.ThreadPoolExecutor(max_workers=1)
        future = executor.submit(self._simplify_fn, svg_text, epsilon_ratio)
        try:
            result = future.result(timeout=self._settings.simplify_timeout_seconds)
        except concurrent.futures.TimeoutError as exc:
            executor.shutdown(wait=False)
            raise SimplificationTimeoutError(
                f"La simplificación tardó más de {self._settings.simplify_timeout_seconds}s y se abortó."
            ) from exc
        executor.shutdown(wait=True)
        return result


def _reduction_percent(node_count_before: int, node_count_after: int) -> float:
    if node_count_before <= 0:
        return 0.0

    reduction = (node_count_before - node_count_after) / node_count_before * 100.0
    # Defensivo: Douglas-Peucker nunca debería AUMENTAR el conteo de nodos,
    # pero se recorta a [0, 100] por si acaso (ej. un SVG de entrada
    # degenerado) para no devolver un porcentaje negativo o >100 a React.
    return max(0.0, min(100.0, reduction))
