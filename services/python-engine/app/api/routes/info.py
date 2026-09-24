from fastapi import APIRouter, Depends

from app.api.dependencies import get_info_service
from app.models.schemas import InfoResponse
from app.services.info_service import InfoService

router = APIRouter(prefix="/api/v1", tags=["info"])


@router.get("/info", response_model=InfoResponse, summary="Información y capacidades del motor")
def get_info(info_service: InfoService = Depends(get_info_service)) -> InfoResponse:
    return info_service.get_info()
