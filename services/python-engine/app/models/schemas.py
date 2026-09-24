"""Modelos tipados de respuesta. Contrato inicial consumido por ASP.NET Core:
GET /health -> { status, service, version }.
"""

from pydantic import BaseModel, Field


class HealthResponse(BaseModel):
    status: str = Field(examples=["ok"])
    service: str = Field(examples=["vectify-python-engine"])
    version: str = Field(examples=["0.1.0"])


class InfoResponse(BaseModel):
    service: str = Field(examples=["vectify-python-engine"])
    version: str = Field(examples=["0.1.0"])
    capabilities: list[str] = Field(
        examples=[["health-check"]],
        description=(
            "Capacidades habilitadas del motor. En este sprint fundacional "
            "no hay vectorización real; solo el chequeo de salud."
        ),
    )


class PreprocessParams(BaseModel):
    """Parámetros ajustables del pipeline de preprocesamiento (M1-S03).

    spec.md no cuantifica rangos numéricos ("Ambigüedades detectadas"); los
    límites de acá son la fuente de verdad del lado Python y deben coincidir
    con los que valida Vectify.Api (Preprocessing/PreprocessOptions) antes de
    llamar a este servicio — documentado como supuesto en el reporte del
    sprint.
    """

    grayscale: bool = Field(False, description="Convierte la imagen a escala de grises")
    contrast: float = Field(1.0, ge=0.5, le=3.0, description="Factor multiplicativo de contraste (1.0 = sin cambio)")
    brightness: int = Field(0, ge=-100, le=100, description="Offset aditivo de brillo (0 = sin cambio)")
    denoise: int = Field(0, ge=0, le=10, description="Intensidad de suavizado/reducción de ruido (0 = sin cambio)")


class PreprocessMetrics(BaseModel):
    mean_brightness: float = Field(examples=[128.4])
    std_dev: float = Field(examples=[42.1])
    min_value: int = Field(examples=[0])
    max_value: int = Field(examples=[255])


class PreprocessResponse(BaseModel):
    """Respuesta de POST /api/v1/preprocess. Consumida únicamente por
    Vectify.Api (el navegador nunca llama directamente a este motor); por eso
    el resultado viaja como imagen embebida en base64 en vez de un archivo
    binario separado, para mantener un único contrato JSON simple de testear
    de forma determinista.
    """

    image_base64: str = Field(description="Preview codificado como PNG, en base64")
    content_type: str = Field(default="image/png", examples=["image/png"])
    width: int
    height: int
    original_width: int
    original_height: int
    effective_params: PreprocessParams
    metrics: PreprocessMetrics


class ThresholdParams(BaseModel):
    """Parámetros ajustables de la etapa de threshold B/N (M1-S04): umbral
    global y su inversión. Rangos alineados con Vectify.Api.Options.ThresholdOptions
    (defensa en profundidad, mismo criterio que PreprocessParams). El modo
    adaptativo (vs. global) se decidió no incluir en este sprint -- ver
    reporte del sprint.
    """

    value: int = Field(128, ge=0, le=255, description="Umbral global (0 = todo negro, 255 = todo blanco)")
    invert: bool = Field(False, description="Invierte blanco/negro del resultado")


class ThresholdMetrics(BaseModel):
    """Porcentaje crudo de píxeles foreground/background de la máscara
    resultante. La clasificación de "casi vacía/casi llena" como advertencia
    se calcula del lado de Vectify.Api (Threshold/ThresholdService.cs), no acá.
    """

    foreground_percent: float = Field(examples=[42.3], ge=0, le=100)
    background_percent: float = Field(examples=[57.7], ge=0, le=100)


class ThresholdResponse(BaseModel):
    """Respuesta de POST /api/v1/threshold. Igual convención que
    PreprocessResponse: consumida únicamente por Vectify.Api, la máscara viaja
    embebida en base64 para mantener un único contrato JSON simple de testear
    de forma determinista.
    """

    image_base64: str = Field(description="Máscara binaria codificada como PNG, en base64")
    content_type: str = Field(default="image/png", examples=["image/png"])
    width: int
    height: int
    effective_params: ThresholdParams
    metrics: ThresholdMetrics


class ErrorResponse(BaseModel):
    """Forma común de error controlado, igual convención que
    Vectify.Api.Contracts.ApiErrorResponse: `code` es estable, `message` es
    para logs/debug humano."""

    code: str = Field(examples=["corrupt_image"])
    message: str
