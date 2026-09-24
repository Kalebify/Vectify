"""Servicio que orquesta el pipeline de preprocesamiento: lectura segura,
validación de dimensiones, transformaciones puras (app.core.pipeline) en un
orden fijo, cálculo de métricas y codificación del preview. Separado de las
funciones puras para poder testearlas de forma aislada (sin Settings/HTTP) y
de las rutas (sin FastAPI) — ver spec.md M1-S03, sección "Python/FastAPI":
"Separar funciones puras y servicio de pipeline".
"""

import base64
import io

import imagesize

from app.core import pipeline
from app.core.config import Settings
from app.core.errors import DimensionsExceededError
from app.models.schemas import PreprocessMetrics, PreprocessParams, PreprocessResponse


class PreprocessingService:
    def __init__(self, settings: Settings) -> None:
        self._settings = settings

    def process(self, data: bytes, params: PreprocessParams) -> PreprocessResponse:
        """Genera un preview a partir de los bytes originales (nunca los
        modifica: se decodifican a un array en memoria, se transforma una
        copia y se codifica un archivo nuevo) y los parámetros ya validados.
        Determinista: mismos `data` + mismos `params` siempre producen el
        mismo `image_base64`.
        """
        self._reject_if_header_dimensions_exceed_limits(data)

        image = pipeline.read_image_safely(data)
        original_height, original_width = image.shape[:2]

        pipeline.check_dimensions(
            image,
            self._settings.max_image_width,
            self._settings.max_image_height,
            self._settings.max_image_pixels,
        )

        # Orden fijo del pipeline: grayscale -> contraste/brillo -> denoise.
        result = pipeline.to_grayscale(image, params.grayscale)
        result = pipeline.adjust_contrast_brightness(result, params.contrast, params.brightness)
        result = pipeline.denoise(result, params.denoise)

        metrics = pipeline.compute_metrics(result)
        encoded_png = pipeline.encode_png(result)
        height, width = result.shape[:2]

        return PreprocessResponse(
            image_base64=base64.b64encode(encoded_png).decode("ascii"),
            content_type="image/png",
            width=width,
            height=height,
            original_width=original_width,
            original_height=original_height,
            effective_params=params,
            metrics=PreprocessMetrics(**metrics),
        )

    def _reject_if_header_dimensions_exceed_limits(self, data: bytes) -> None:
        """Lee ancho/alto directamente de la cabecera del archivo (PNG/JPEG/
        WEBP), SIN decodificar los píxeles, y rechaza temprano si ya excede los
        límites configurados. Esto evita agotar memoria decodificando por
        completo una imagen comprimida chica pero con dimensiones declaradas
        enormes (bomba de descompresión) ANTES de que pipeline.check_dimensions
        (que corre después de decodificar) tenga oportunidad de actuar.

        Si la cabecera no se puede parsear de forma confiable (formato no
        reconocido o corrupto), no falla acá: imagesize.get devuelve (-1, -1) y
        el flujo sigue su curso normal, dejando que read_image_safely +
        pipeline.check_dimensions actúen como red de seguridad (defensa en
        profundidad, no se reemplaza ese chequeo posterior).
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
