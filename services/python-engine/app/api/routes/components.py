from fastapi import APIRouter, Depends, File, Form, UploadFile
from pydantic import ValidationError

from app.api.dependencies import get_component_analysis_service
from app.core.errors import InvalidParametersError
from app.models.schemas import ComponentAnalysisParams, ComponentAnalysisResponse, ErrorResponse
from app.services.component_analysis_service import ComponentAnalysisService

router = APIRouter(prefix="/api/v1", tags=["components"])


@router.post(
    "/components",
    response_model=ComponentAnalysisResponse,
    summary="Componentes independientes por capa: detecta piezas físicas geométricamente separadas en un SVG ya vectorizado",
    description=(
        "Recibe un SVG YA generado por vectorización (una capa de un solo color, M2-S02; multipart, "
        "campo 'file') y los parámetros de tolerancia (campo 'params', JSON serializado de "
        "ComponentAnalysisParams: touch_ratio, tiny_area_ratio, ambos relativos al tamaño de la "
        "capa). Análisis de SOLO LECTURA: nunca modifica el SVG recibido ni une/separa geometría "
        "-- solo INFORMA la estructura física ya existente. Solo lo llama Vectify.Api."
    ),
    responses={
        400: {"model": ErrorResponse, "description": "SVG de entrada corrupto/no decodificable o no es XML/SVG válido"},
        413: {"model": ErrorResponse, "description": "SVG de entrada demasiado grande, o con demasiados subpaths analizables"},
        422: {"model": ErrorResponse, "description": "Parámetros fuera de rango o inválidos"},
        500: {"model": ErrorResponse, "description": "Error inesperado al analizar el SVG"},
        504: {"model": ErrorResponse, "description": "El análisis excedió el tiempo máximo configurado"},
    },
)
async def analyze_components(
    file: UploadFile = File(..., description="SVG de una capa vectorial ya generada (campo 'file')"),
    params: str = Form(..., description="JSON serializado de ComponentAnalysisParams"),
    service: ComponentAnalysisService = Depends(get_component_analysis_service),
) -> ComponentAnalysisResponse:
    try:
        parsed_params = ComponentAnalysisParams.model_validate_json(params)
    except ValidationError as exc:
        raise InvalidParametersError(f"Parámetros del análisis de componentes inválidos: {exc}") from exc

    data = await file.read()
    return service.process(data, parsed_params)
