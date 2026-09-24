"""Punto de entrada del motor Python/FastAPI.

Ejecución local: uvicorn app.main:app --reload --host 0.0.0.0 --port 8000
(o python -m app.main, que respeta HOST/PORT de la configuración).
"""

import logging

from fastapi import FastAPI, Request
from fastapi.responses import JSONResponse

from app.api.routes.health import router as health_router
from app.api.routes.info import router as info_router
from app.api.routes.preprocess import router as preprocess_router
from app.core.config import get_settings
from app.core.errors import CorruptImageError, DimensionsExceededError, InvalidParametersError, PreprocessingError
from app.core.logging import configure_logging

settings = get_settings()
configure_logging(settings.log_level)
logger = logging.getLogger("app.main")

app = FastAPI(
    title="Vectify — Motor Python",
    description=(
        "Microservicio de procesamiento/vectorización. Expone chequeos de salud, "
        "información del servicio y el pipeline determinista de preprocesamiento "
        "de imágenes (grayscale, contraste/brillo, suavizado/denoise)."
    ),
    version=settings.service_version,
)

app.include_router(health_router)
app.include_router(info_router)
app.include_router(preprocess_router)

# Códigos HTTP por tipo de error controlado del pipeline de preprocesamiento
# (ver "Errores y límites" de spec.md): imagen corrupta -> 400, dimensiones
# excesivas -> 413, parámetros inválidos -> 422. Cualquier otro
# PreprocessingError (memoria, fallo inesperado de OpenCV) cae a 500.
_STATUS_BY_ERROR: dict[type[PreprocessingError], int] = {
    CorruptImageError: 400,
    DimensionsExceededError: 413,
    InvalidParametersError: 422,
}


@app.exception_handler(PreprocessingError)
async def preprocessing_error_handler(request: Request, exc: PreprocessingError) -> JSONResponse:
    status_code = _STATUS_BY_ERROR.get(type(exc), 500)
    return JSONResponse(status_code=status_code, content={"code": exc.code, "message": str(exc)})


@app.exception_handler(Exception)
async def unhandled_exception_handler(request: Request, exc: Exception) -> JSONResponse:
    # Red de seguridad: un fallo inesperado (memoria, error interno de OpenCV,
    # etc.) nunca debe filtrar un stack trace ni devolver el 500 sin formato
    # por defecto de FastAPI; se registra server-side y se responde con la
    # misma forma {code, message} que el resto de errores controlados.
    logger.exception("Error inesperado no controlado: %s", exc)
    return JSONResponse(
        status_code=500,
        content={"code": "processing_error", "message": "Ocurrió un error inesperado al procesar la solicitud."},
    )


if __name__ == "__main__":
    import uvicorn

    uvicorn.run("app.main:app", host=settings.host, port=settings.port)
