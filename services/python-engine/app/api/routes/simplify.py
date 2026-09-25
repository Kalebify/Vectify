from fastapi import APIRouter, Depends, File, Form, UploadFile
from pydantic import ValidationError

from app.api.dependencies import get_simplification_service
from app.core.errors import InvalidParametersError
from app.models.schemas import ErrorResponse, SimplifyParams, SimplifyResponse
from app.services.simplification_service import SimplificationService

router = APIRouter(prefix="/api/v1", tags=["simplify"])


@router.post(
    "/simplify",
    response_model=SimplifyResponse,
    summary="Reduce la cantidad de nodos de un SVG ya vectorizado (Douglas-Peucker)",
    description=(
        "Recibe un SVG YA generado por vectorización -- o por una simplificación previa -- "
        "(multipart, campo 'file') y los parámetros de simplificación (campo 'params', JSON "
        "serializado de SimplifyParams: epsilon_ratio, relativo a la diagonal del bounding box "
        "del SVG). No recibe el nombre del preset (Bajo/Medio/Alto): Vectify.Api ya lo resolvió "
        "a un epsilon_ratio numérico antes de llamar acá. El SVG resultante se re-sanitiza y "
        "se acompaña de nodeCount antes/después y % de reducción. Solo lo llama Vectify.Api."
    ),
    responses={
        400: {"model": ErrorResponse, "description": "SVG de entrada corrupto/no decodificable o no es XML/SVG válido"},
        413: {"model": ErrorResponse, "description": "SVG de entrada (o de salida) demasiado grande"},
        422: {"model": ErrorResponse, "description": "Parámetros fuera de rango o inválidos"},
        500: {"model": ErrorResponse, "description": "Error inesperado al simplificar"},
        504: {"model": ErrorResponse, "description": "La simplificación excedió el tiempo máximo configurado"},
    },
)
async def simplify_svg(
    file: UploadFile = File(..., description="SVG ya vectorizado (campo 'file')"),
    params: str = Form(..., description="JSON serializado de SimplifyParams"),
    service: SimplificationService = Depends(get_simplification_service),
) -> SimplifyResponse:
    try:
        parsed_params = SimplifyParams.model_validate_json(params)
    except ValidationError as exc:
        raise InvalidParametersError(f"Parámetros de simplificación inválidos: {exc}") from exc

    data = await file.read()
    return service.process(data, parsed_params)
