from fastapi import APIRouter, Depends, File, Form, UploadFile
from pydantic import ValidationError

from app.api.dependencies import get_color_palette_service
from app.core.errors import InvalidParametersError
from app.models.schemas import ColorPaletteParams, ColorPaletteResponse, ErrorResponse
from app.services.color_palette_service import ColorPaletteService

router = APIRouter(prefix="/api/v1", tags=["color-palette"])


@router.post(
    "/color-palette",
    response_model=ColorPaletteResponse,
    summary="Detecta/reduce la paleta de colores dominantes de una imagen (M2-S01)",
    description=(
        "Recibe la misma imagen de entrada que ya usa el pipeline de preprocesamiento/threshold "
        "(multipart, campo 'file', RGBA soportado) y los parámetros de detección (campo 'params', "
        "JSON serializado de ColorPaletteParams: tolerance, max_colors). Clusteriza los colores en "
        "espacio Lab de forma determinista (ver app.core.color_palette_pipeline) y devuelve la "
        "paleta detectada (color, % de área, máscara por grupo) junto con un preview cuantizado. "
        "Los píxeles con alpha=0 se excluyen por completo (nunca cuentan como color). Solo lo llama "
        "Vectify.Api, que ya validó los rangos antes de reenviar la solicitud acá."
    ),
    responses={
        400: {"model": ErrorResponse, "description": "Imagen corrupta o no decodificable"},
        413: {"model": ErrorResponse, "description": "Dimensiones de la imagen excesivas"},
        422: {"model": ErrorResponse, "description": "Parámetros fuera de rango o inválidos"},
        500: {"model": ErrorResponse, "description": "Error inesperado al detectar la paleta"},
        504: {"model": ErrorResponse, "description": "La detección excedió el tiempo máximo configurado"},
    },
)
async def detect_color_palette(
    file: UploadFile = File(..., description="Imagen original (PNG/JPG/WEBP), RGBA soportado"),
    params: str = Form(..., description="JSON serializado de ColorPaletteParams"),
    service: ColorPaletteService = Depends(get_color_palette_service),
) -> ColorPaletteResponse:
    try:
        parsed_params = ColorPaletteParams.model_validate_json(params)
    except ValidationError as exc:
        raise InvalidParametersError(f"Parámetros de paleta de colores inválidos: {exc}") from exc

    data = await file.read()
    return service.process(data, parsed_params)
