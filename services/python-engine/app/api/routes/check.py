from fastapi import APIRouter, Depends, File, Form, UploadFile
from pydantic import ValidationError

from app.api.dependencies import get_path_checker_service
from app.core.errors import InvalidParametersError
from app.models.schemas import CheckParams, CheckResponse, ErrorResponse
from app.services.path_checker_service import PathCheckerService

router = APIRouter(prefix="/api/v1", tags=["check"])


@router.post(
    "/check",
    response_model=CheckResponse,
    summary="Laser Checker: detecta paths abiertos y duplicados/casi-duplicados en un SVG ya vectorizado",
    description=(
        "Recibe un SVG YA generado por vectorización o por una simplificación previa (multipart, "
        "campo 'file') y los parámetros de tolerancia (campo 'params', JSON serializado de "
        "CheckParams: close_gap_ratio, duplicate_point_ratio, ambas relativas a la diagonal del "
        "bounding box del SVG). Análisis de SOLO LECTURA: nunca modifica el SVG recibido ni "
        "produce uno nuevo. Solo lo llama Vectify.Api."
    ),
    responses={
        400: {"model": ErrorResponse, "description": "SVG de entrada corrupto/no decodificable o no es XML/SVG válido"},
        413: {"model": ErrorResponse, "description": "SVG de entrada demasiado grande, o con demasiados subpaths analizables"},
        422: {"model": ErrorResponse, "description": "Parámetros fuera de rango o inválidos"},
        500: {"model": ErrorResponse, "description": "Error inesperado al analizar el SVG"},
        504: {"model": ErrorResponse, "description": "El análisis excedió el tiempo máximo configurado"},
    },
)
async def check_svg(
    file: UploadFile = File(..., description="SVG ya vectorizado o simplificado (campo 'file')"),
    params: str = Form(..., description="JSON serializado de CheckParams"),
    service: PathCheckerService = Depends(get_path_checker_service),
) -> CheckResponse:
    try:
        parsed_params = CheckParams.model_validate_json(params)
    except ValidationError as exc:
        raise InvalidParametersError(f"Parámetros del checker de paths inválidos: {exc}") from exc

    data = await file.read()
    return service.process(data, parsed_params)
