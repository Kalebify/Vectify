from fastapi import APIRouter, Depends, File, Form, UploadFile
from pydantic import ValidationError

from app.api.dependencies import get_threshold_service
from app.core.errors import InvalidParametersError
from app.models.schemas import ErrorResponse, ThresholdParams, ThresholdResponse
from app.services.threshold_service import ThresholdingService

router = APIRouter(prefix="/api/v1", tags=["threshold"])


@router.post(
    "/threshold",
    response_model=ThresholdResponse,
    summary="Genera una máscara binaria (threshold global, con inversión opcional) a partir de un preview",
    description=(
        "Recibe el preview YA preprocesado (multipart, campo 'file') y los parámetros de "
        "threshold (campo 'params', JSON serializado de ThresholdParams). Nunca modifica el "
        "archivo recibido: decodifica una copia en memoria, aplica el umbral y devuelve una "
        "máscara nueva en base64 junto con las métricas de porcentaje foreground/background. "
        "Solo lo llama Vectify.Api, que ya validó los rangos antes de reenviar la solicitud acá."
    ),
    responses={
        400: {"model": ErrorResponse, "description": "Imagen corrupta o no decodificable"},
        413: {"model": ErrorResponse, "description": "Dimensiones de la imagen excesivas"},
        422: {"model": ErrorResponse, "description": "Parámetros fuera de rango o inválidos"},
        500: {"model": ErrorResponse, "description": "Error inesperado al procesar la imagen"},
    },
)
async def threshold_image(
    file: UploadFile = File(..., description="Preview ya preprocesado (PNG/JPG/WEBP)"),
    params: str = Form(..., description="JSON serializado de ThresholdParams"),
    service: ThresholdingService = Depends(get_threshold_service),
) -> ThresholdResponse:
    try:
        parsed_params = ThresholdParams.model_validate_json(params)
    except ValidationError as exc:
        raise InvalidParametersError(f"Parámetros de threshold inválidos: {exc}") from exc

    data = await file.read()
    return service.process(data, parsed_params)
