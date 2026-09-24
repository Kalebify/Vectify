"""Configuración del motor Python, leída de variables de entorno (o un
archivo .env local). Nada crítico queda hardcodeado: host, puerto, nombre
de servicio, versión y nivel de log son todos configurables.
"""

from functools import lru_cache

from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_file=".env", env_file_encoding="utf-8", extra="ignore")

    service_name: str = "vectify-python-engine"
    service_version: str = "0.1.0"
    host: str = "0.0.0.0"
    port: int = 8000
    log_level: str = "info"

    # Límites del pipeline de preprocesamiento (M1-S03). spec.md no cuantifica
    # "dimensiones excesivas"; estos valores son un supuesto documentado (ver
    # reporte del sprint): suficientes para imágenes de trabajo típicas de
    # tracing/vectorización sin arriesgar agotar memoria en un solo proceso.
    max_image_width: int = 6000
    max_image_height: int = 6000
    max_image_pixels: int = 25_000_000


@lru_cache
def get_settings() -> Settings:
    return Settings()
