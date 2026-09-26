from fastapi import APIRouter, Depends, File, Form, UploadFile
from pydantic import ValidationError

from app.api.dependencies import get_physical_union_service
from app.core.errors import InvalidParametersError
from app.models.schemas import ErrorResponse, PhysicalUnionParams, PhysicalUnionResponse
from app.services.physical_union_service import PhysicalUnionService

router = APIRouter(prefix="/api/v1", tags=["physical-union"])


@router.post(
    "/components/union",
    response_model=PhysicalUnionResponse,
    summary="Unión física de piezas: fusiona 2+ componentes físicos ya calculados (M2-S03) en una única pieza fabricable",
    description=(
        "Recibe un SVG YA generado por vectorización (una capa de un solo color, M2-S02; multipart, "
        "campo 'file') y la selección de componentes a fusionar (campo 'params', JSON serializado de "
        "PhysicalUnionParams: selections, touch_ratio, tiny_area_ratio, bridge_width_ratio). A "
        "diferencia de /api/v1/components (M2-S03, solo lectura), SÍ modifica geometría: usa unión "
        "booleana para piezas solapadas/tangentes y un bridge simple/directo para piezas separadas "
        "(ver app.core.physical_union). Nunca finge una unión: el resultado se revalida con el MISMO "
        "analizador de componentes de M2-S03 antes de responder -- si el conteo no coincide con el "
        "esperado, responde 422 (physical_union_impossible) en vez de un SVG que 'parece' unido. Solo "
        "lo llama Vectify.Api."
    ),
    responses={
        400: {"model": ErrorResponse, "description": "SVG de entrada corrupto/no decodificable o no es XML/SVG válido"},
        413: {"model": ErrorResponse, "description": "SVG de entrada demasiado grande, o con demasiados subpaths analizables"},
        422: {
            "model": ErrorResponse,
            "description": (
                "Parámetros/selección inválidos, geometría de alguna pieza seleccionada autointersectante/"
                "degenerada, o la unión no fue geométricamente posible (validación post-operación)"
            ),
        },
        500: {"model": ErrorResponse, "description": "Error inesperado al unir las piezas"},
        504: {"model": ErrorResponse, "description": "La unión excedió el tiempo máximo configurado"},
    },
)
async def union_components(
    file: UploadFile = File(..., description="SVG de una capa vectorial ya generada (campo 'file')"),
    params: str = Form(..., description="JSON serializado de PhysicalUnionParams"),
    service: PhysicalUnionService = Depends(get_physical_union_service),
) -> PhysicalUnionResponse:
    try:
        parsed_params = PhysicalUnionParams.model_validate_json(params)
    except ValidationError as exc:
        raise InvalidParametersError(f"Parámetros de unión física inválidos: {exc}") from exc

    data = await file.read()
    return service.process(data, parsed_params)
