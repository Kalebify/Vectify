from fastapi import Depends

from app.core.config import Settings, get_settings
from app.services.info_service import InfoService


def get_info_service(settings: Settings = Depends(get_settings)) -> InfoService:
    return InfoService(settings)
