from fastapi import Depends

from app.core.config import Settings, get_settings
from app.services.info_service import InfoService
from app.services.preprocessing_service import PreprocessingService
from app.services.threshold_service import ThresholdingService
from app.services.vectorization_service import VectorizationService


def get_info_service(settings: Settings = Depends(get_settings)) -> InfoService:
    return InfoService(settings)


def get_preprocessing_service(settings: Settings = Depends(get_settings)) -> PreprocessingService:
    return PreprocessingService(settings)


def get_threshold_service(settings: Settings = Depends(get_settings)) -> ThresholdingService:
    return ThresholdingService(settings)


def get_vectorization_service(settings: Settings = Depends(get_settings)) -> VectorizationService:
    return VectorizationService(settings)
