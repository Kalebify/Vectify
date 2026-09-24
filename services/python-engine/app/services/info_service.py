"""Lógica de negocio para salud/información del motor. Separado de las rutas
para poder testearlo de forma aislada y para dejar un punto de extensión
claro cuando en sprints futuros se agreguen capacidades reales
(OpenCV, VTracer, Potrace, etc.).
"""

from app.core.config import Settings
from app.models.schemas import HealthResponse, InfoResponse


class InfoService:
    def __init__(self, settings: Settings) -> None:
        self._settings = settings

    def get_health(self) -> HealthResponse:
        return HealthResponse(
            status="ok",
            service=self._settings.service_name,
            version=self._settings.service_version,
        )

    def get_info(self) -> InfoResponse:
        return InfoResponse(
            service=self._settings.service_name,
            version=self._settings.service_version,
            capabilities=["health-check"],
        )
