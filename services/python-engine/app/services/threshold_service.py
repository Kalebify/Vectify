"""Servicio que orquesta la etapa de threshold B/N: lectura segura del
preview YA preprocesado recibido, validación de dimensiones (defensa en
profundidad), reducción a un canal de gris, umbral global determinista (con
inversión opcional) y cálculo de métricas de porcentaje foreground/
background. Separado de PreprocessingService por ser una etapa distinta del
pipeline (ver app.core.threshold_pipeline) -- ver spec.md M1-S04.
"""

import base64
import io

import imagesize

from app.core import pipeline, threshold_pipeline
from app.core.config import Settings
from app.core.errors import DimensionsExceededError
from app.models.schemas import ThresholdMetrics, ThresholdParams, ThresholdResponse


class ThresholdingService:
    def __init__(self, settings: Settings) -> None:
        self._settings = settings

    def process(self, data: bytes, params: ThresholdParams) -> ThresholdResponse:
        """Genera una máscara binaria a partir de los bytes recibidos (el
        preview ya preprocesado, nunca el original crudo -- eso lo decide
        Vectify.Api antes de llamar acá) y los parámetros ya validados.
        Determinista: mismos `data` + mismos `params` siempre producen el
        mismo `image_base64` y las mismas métricas.
        """
        self._reject_if_header_dimensions_exceed_limits(data)

        # A diferencia de Preprocessing/Vectorization (que usan
        # pipeline.read_image_safely, IMREAD_COLOR, sin canal alpha),
        # Threshold decodifica preservando alpha: un píxel completamente
        # transparente debe quedar como background sin importar el color RGB
        # que tenga "debajo" -- ver threshold_pipeline.apply_alpha_as_background.
        image = threshold_pipeline.read_image_with_alpha(data)

        pipeline.check_dimensions(
            image,
            self._settings.max_image_width,
            self._settings.max_image_height,
            self._settings.max_image_pixels,
        )

        bgr_image, alpha = threshold_pipeline.split_alpha_channel(image)

        gray = threshold_pipeline.to_grayscale_single_channel(bgr_image)
        mask = threshold_pipeline.apply_threshold(gray, params.value, params.invert)
        mask = threshold_pipeline.apply_alpha_as_background(mask, alpha)

        metrics = threshold_pipeline.compute_threshold_metrics(mask)
        encoded_png = pipeline.encode_png(mask)
        height, width = mask.shape[:2]

        return ThresholdResponse(
            image_base64=base64.b64encode(encoded_png).decode("ascii"),
            content_type="image/png",
            width=width,
            height=height,
            effective_params=params,
            metrics=ThresholdMetrics(**metrics),
        )

    def _reject_if_header_dimensions_exceed_limits(self, data: bytes) -> None:
        """Mismo criterio de defensa contra "bombas de descompresión" que
        PreprocessingService._reject_if_header_dimensions_exceed_limits: lee
        ancho/alto de la cabecera sin decodificar píxeles y rechaza temprano
        si ya excede los límites configurados. Duplicado deliberadamente (en
        vez de compartir el método privado) para mantener cada servicio
        independiente y no tocar PreprocessingService, ya corregido y
        probado en la ronda anterior.
        """
        width, height = imagesize.get(io.BytesIO(data))
        if width <= 0 or height <= 0:
            return

        total_pixels = width * height
        max_width = self._settings.max_image_width
        max_height = self._settings.max_image_height
        max_pixels = self._settings.max_image_pixels

        if width > max_width or height > max_height or total_pixels > max_pixels:
            raise DimensionsExceededError(
                f"La imagen ({width}x{height}, {total_pixels} px) supera el límite permitido "
                f"({max_width}x{max_height}, {max_pixels} px totales) según su cabecera, "
                "sin necesidad de decodificarla por completo."
            )
