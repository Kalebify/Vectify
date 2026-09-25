"""Lógica de negocio para salud/información del motor. Separado de las rutas
para poder testearlo de forma aislada. `capabilities` debe reflejar los tags
de router realmente montados en app.main (ver app/api/routes/*.py) -- se
quedó desactualizado (solo "health-check") entre M1-S03 y M1-S08 porque
ningún sprint lo tocó al agregar su endpoint; corregido para incluir todas
las etapas reales del pipeline.
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
            capabilities=[
                "health-check",
                "preprocess",
                "threshold",
                "vectorize",
                "simplify",
                "check",
            ],
        )
