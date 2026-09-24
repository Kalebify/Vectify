"""Servicio que orquesta el pipeline de preprocesamiento: lectura segura,
validación de dimensiones, transformaciones puras (app.core.pipeline) en un
orden fijo, cálculo de métricas y codificación del preview. Separado de las
funciones puras para poder testearlas de forma aislada (sin Settings/HTTP) y
de las rutas (sin FastAPI) — ver spec.md M1-S03, sección "Python/FastAPI":
"Separar funciones puras y servicio de pipeline".
"""

import base64

from app.core import pipeline
from app.core.config import Settings
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
