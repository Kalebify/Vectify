import json

from fastapi import APIRouter, Depends, File, Form, UploadFile

from app.api.dependencies import get_vectorization_service
from app.core.errors import InvalidParametersError
from app.models.schemas import ErrorResponse, VectorizeLayersResponse, VectorizeResponse, VectorLayerItem
from app.services.vectorization_service import VectorizationService

router = APIRouter(prefix="/api/v1", tags=["vectorize"])


@router.post(
    "/vectorize",
    response_model=VectorizeResponse,
    summary="Vectoriza una máscara B/N (VTracer) y devuelve un SVG sanitizado",
    description=(
        "Recibe la máscara B/N YA generada por threshold (M1-S04, multipart, campo 'file'). "
        "No recibe parámetros ajustables en este sprint: el motor de trazado está encapsulado "
        "detrás de app.core.vector_engine.VectorEngine (VTracer, ver spec.md, 'Decisión "
        "bloqueante resuelta con el usuario') con una configuración fija y determinista. "
        "El SVG resultante se sanitiza (sin <script>, sin referencias externas) antes de "
        "devolverse. Solo lo llama Vectify.Api."
    ),
    responses={
        400: {"model": ErrorResponse, "description": "Máscara corrupta o no decodificable"},
        413: {"model": ErrorResponse, "description": "Dimensiones de la máscara excesivas, o SVG resultante demasiado grande"},
        422: {"model": ErrorResponse, "description": "La máscara no tiene ningún píxel de foreground (vacía)"},
        500: {"model": ErrorResponse, "description": "Error inesperado del motor de trazado"},
        504: {"model": ErrorResponse, "description": "El trazado excedió el tiempo máximo configurado"},
    },
)
async def vectorize_mask(
    file: UploadFile = File(..., description="Máscara B/N ya generada por threshold (PNG)"),
    service: VectorizationService = Depends(get_vectorization_service),
) -> VectorizeResponse:
    data = await file.read()
    return service.process(data)


@router.post(
    "/vectorize-layers",
    response_model=VectorizeLayersResponse,
    summary="Vectoriza N máscaras de color de forma independiente en una sola llamada (M2-S02)",
    description=(
        "Recibe N máscaras B/N (multipart, campo repetido 'files', una por ColorGroup YA "
        "confirmado -- M2-S01) y sus IDs de grupo correspondientes (campo 'group_ids', JSON "
        "array de strings, mismo orden y cantidad que 'files'). Vectoriza cada máscara de forma "
        "INDEPENDIENTE reutilizando VectorizationService.process (el mismo motor/pipeline de "
        "M1-S05, sin reinventar el trazado de contornos) N veces DENTRO de esta única request -- "
        "evita N round-trips HTTP separados entre Vectify.Api y este motor. Ninguna máscara se "
        "recorta a su bounding box: todas comparten las dimensiones de la imagen original, así "
        "que los SVG resultantes ya comparten el mismo sistema de coordenadas/viewBox sin "
        "normalización adicional. Si cualquier máscara falla (corrupta, vacía, demasiado grande, "
        "timeout), toda la solicitud falla -- el conjunto de capas se genera todo o nada, nunca "
        "parcial. Solo lo llama Vectify.Api."
    ),
    responses={
        400: {"model": ErrorResponse, "description": "Alguna máscara es corrupta o no decodificable"},
        413: {"model": ErrorResponse, "description": "Alguna máscara excede las dimensiones máximas, o su SVG resultante es demasiado grande"},
        422: {"model": ErrorResponse, "description": "'group_ids' inválido/no coincide con 'files', o alguna máscara no tiene foreground"},
        500: {"model": ErrorResponse, "description": "Error inesperado del motor de trazado"},
        504: {"model": ErrorResponse, "description": "El trazado de alguna máscara excedió el tiempo máximo configurado"},
    },
)
async def vectorize_layers(
    files: list[UploadFile] = File(..., description="Máscaras B/N (PNG), una por ColorGroup"),
    group_ids: str = Form(..., description="JSON array de IDs (string) de ColorGroup, mismo orden/cantidad que 'files'"),
    service: VectorizationService = Depends(get_vectorization_service),
) -> VectorizeLayersResponse:
    try:
        parsed_group_ids = json.loads(group_ids)
    except json.JSONDecodeError as exc:
        raise InvalidParametersError(f"'group_ids' no es JSON válido: {exc}") from exc

    if not isinstance(parsed_group_ids, list) or not all(
        isinstance(item, str) and item for item in parsed_group_ids
    ):
        raise InvalidParametersError("'group_ids' debe ser un array JSON de strings no vacíos.")

    if len(parsed_group_ids) != len(files):
        raise InvalidParametersError(
            f"La cantidad de group_ids ({len(parsed_group_ids)}) no coincide con la cantidad de "
            f"archivos recibidos ({len(files)})."
        )

    if len(files) == 0:
        raise InvalidParametersError("Debe enviarse al menos una máscara para vectorizar.")

    layers: list[VectorLayerItem] = []
    for group_id, file in zip(parsed_group_ids, files):
        data = await file.read()
        result = service.process(data)
        layers.append(
            VectorLayerItem(
                group_id=group_id,
                svg=result.svg,
                content_type=result.content_type,
                width=result.width,
                height=result.height,
                metrics=result.metrics,
            )
        )

    return VectorizeLayersResponse(layers=layers)
