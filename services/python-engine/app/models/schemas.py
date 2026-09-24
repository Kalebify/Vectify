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


class ErrorResponse(BaseModel):
    """Forma común de error controlado, igual convención que
    Vectify.Api.Contracts.ApiErrorResponse: `code` es estable, `message` es
    para logs/debug humano."""

    code: str = Field(examples=["corrupt_image"])
    message: str
