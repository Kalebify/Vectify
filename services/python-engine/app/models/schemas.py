"""Modelos tipados de respuesta. Contrato inicial consumido por ASP.NET Core:
GET /health -> { status, service, version }.
"""

from pydantic import BaseModel, Field


class HealthResponse(BaseModel):
    status: str = Field(examples=["ok"])
    service: str = Field(examples=["vectify-python-engine"])
    version: str = Field(examples=["0.1.0"])


class InfoResponse(BaseModel):
    service: str = Field(examples=["vectify-python-engine"])
    version: str = Field(examples=["0.1.0"])
    capabilities: list[str] = Field(
        examples=[["health-check"]],
        description=(
            "Capacidades habilitadas del motor. En este sprint fundacional "
            "no hay vectorización real; solo el chequeo de salud."
        ),
    )
