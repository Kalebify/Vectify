"""Punto de entrada del motor Python/FastAPI.

Ejecución local: uvicorn app.main:app --reload --host 0.0.0.0 --port 8000
(o python -m app.main, que respeta HOST/PORT de la configuración).
"""

from fastapi import FastAPI

from app.api.routes.health import router as health_router
from app.api.routes.info import router as info_router
from app.core.config import get_settings
from app.core.logging import configure_logging

settings = get_settings()
configure_logging(settings.log_level)

app = FastAPI(
    title="Vectify — Motor Python",
    description=(
        "Microservicio de procesamiento/vectorización. Sprint fundacional: "
        "solo expone chequeos de salud e información del servicio."
    ),
    version=settings.service_version,
)

app.include_router(health_router)
app.include_router(info_router)


if __name__ == "__main__":
    import uvicorn

    uvicorn.run("app.main:app", host=settings.host, port=settings.port)
