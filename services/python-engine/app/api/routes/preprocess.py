from fastapi import APIRouter, Depends, File, Form, UploadFile
from pydantic import ValidationError

from app.api.dependencies import get_preprocessing_service
from app.core.errors import InvalidParametersError
from app.models.schemas import ErrorResponse, PreprocessParams, PreprocessResponse
from app.services.preprocessing_service import PreprocessingService

router = APIRouter(prefix="/api/v1", tags=["preprocess"])


@router.post(
    "/preprocess",
    response_model=PreprocessResponse,
    summary="Genera un preview preprocesado (grayscale, contraste/brillo, suavizado/denoise)",
    description=(
        "Recibe la imagen original (multipart, campo 'file') y los parámetros del "
        "pipeline (campo 'params', JSON serializado de PreprocessParams). Nunca "
        "modifica el archivo recibido: decodifica una copia en memoria, aplica las "
        "transformaciones y devuelve un preview nuevo en base64. Solo lo llama "
        "Vectify.Api, que ya validó los rangos antes de reenviar la solicitud acá."
    ),
    responses={
        400: {"model": ErrorResponse, "description": "Imagen corrupta o no decodificable"},
        413: {"model": ErrorResponse, "description": "Dimensiones de la imagen excesivas"},
        422: {"model": ErrorResponse, "description": "Parámetros fuera de rango o inválidos"},
        500: {"model": ErrorResponse, "description": "Error inesperado al procesar la imagen"},
    },
)
async def preprocess_image(
    file: UploadFile = File(..., description="Imagen original (PNG/JPG/WEBP)"),
    params: str = Form(..., description="JSON serializado de PreprocessParams"),
    service: PreprocessingService = Depends(get_preprocessing_service),
) -> PreprocessResponse:
    try:
        parsed_params = PreprocessParams.model_validate_json(params)
    except ValidationError as exc:
        raise InvalidParametersError(f"Parámetros de preprocesamiento inválidos: {exc}") from exc

    data = await file.read()
    return service.process(data, parsed_params)
