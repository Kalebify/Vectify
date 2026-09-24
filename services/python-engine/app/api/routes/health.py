from fastapi import APIRouter, Depends

from app.api.dependencies import get_info_service
from app.models.schemas import HealthResponse
from app.services.info_service import InfoService

router = APIRouter(tags=["health"])


@router.get("/health", response_model=HealthResponse, summary="Chequeo de salud del motor Python")
def get_health(info_service: InfoService = Depends(get_info_service)) -> HealthResponse:
    return info_service.get_health()
