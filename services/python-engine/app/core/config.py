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

    # Límites de la etapa de vectorización (M1-S05). spec.md tampoco los
    # cuantifica ("Ambigüedades detectadas" en spec.md); supuesto documentado
    # en el reporte del sprint. vectorize_timeout_seconds es un presupuesto
    # interno del proceso Python (ver app.services.vectorization_service),
    # independiente y menor al timeout HTTP configurado del lado de
    # Vectify.Api (Vectorize:TimeoutSeconds), para que el error tipado de
    # Python llegue a tiempo en vez de que el cliente HTTP corte primero.
    vectorize_timeout_seconds: int = 25
    max_svg_output_bytes: int = 5_000_000

    # Límites de la etapa de simplificación de nodos (M1-S07). spec.md
    # tampoco los cuantifica ("Valores numéricos concretos de los presets
    # Bajo/Medio/Alto ... no bloqueante, el implementador elige y documenta");
    # supuesto documentado en el reporte del sprint. simplify_timeout_seconds
    # es un presupuesto interno del proceso Python (ver
    # app.services.simplification_service), independiente y menor al timeout
    # HTTP configurado del lado de Vectify.Api (Simplify:TimeoutSeconds), mismo
    # criterio que vectorize_timeout_seconds. El SVG de entrada reutiliza
    # max_svg_output_bytes como límite de tamaño (nunca debería ser más grande
    # que el límite que ya se le aplicó al generarlo).
    simplify_timeout_seconds: int = 15


@lru_cache
def get_settings() -> Settings:
    return Settings()
