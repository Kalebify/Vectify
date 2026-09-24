from fastapi import APIRouter, Depends, File, UploadFile

from app.api.dependencies import get_vectorization_service
from app.models.schemas import ErrorResponse, VectorizeResponse
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
