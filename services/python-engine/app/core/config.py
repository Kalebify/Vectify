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

    # Límites del Laser Checker de paths abiertos/duplicados (M1-S08). spec.md
    # tampoco los cuantifica ("Valor(es) de tolerancia por defecto no están
    # cuantificados -- no bloqueante, el implementador elige y documenta");
    # supuesto documentado en el reporte del sprint. check_timeout_seconds es
    # un presupuesto interno del proceso Python (ver
    # app.services.path_checker_service), mismo criterio que
    # simplify_timeout_seconds. max_check_subpaths acota la cantidad de
    # subpaths analizables antes de intentar la detección de duplicados
    # (O(n^2) sobre esa cantidad) -- ver app.core.errors.TooManySubpathsError.
    check_timeout_seconds: int = 15
    max_check_subpaths: int = 20_000

    # Detección/reducción de paleta de colores (M2-S01). spec.md tampoco
    # cuantifica tolerancia/número objetivo de colores ("Ambigüedades
    # detectadas": "el implementador decide y documenta"); supuestos
    # documentados en el reporte del sprint. color_palette_timeout_seconds es
    # un presupuesto interno del proceso Python (mismo criterio que
    # simplify_timeout_seconds/check_timeout_seconds): el clustering es CPU-
    # bound puro Python/NumPy, se acota con un hilo separado.
    color_palette_timeout_seconds: int = 20

    # Tolerancia por defecto (distancia euclídea en espacio Lab -- ver
    # app.core.color_palette_pipeline) para fusionar automáticamente dos
    # colores "casi iguales" durante el clustering determinista. El rango
    # [0, 100] cubre holgadamente el espacio Lab de OpenCV (L,a,b en [0,255]
    # tras su escalado a 8 bits): una tolerancia de 100 ya fusiona casi
    # cualquier par de colores.
    color_palette_default_tolerance: float = 12.0
    color_palette_min_tolerance: float = 0.0
    color_palette_max_tolerance: float = 100.0

    # Límite superior opcional de colores en la paleta resultante (fusiona
    # los clusters más parecidos entre sí hasta entrar en el presupuesto).
    color_palette_min_colors: int = 1
    color_palette_max_colors_upper_bound: int = 64

    # Componentes físicos independientes por capa (M2-S03). spec.md no
    # cuantifica el criterio exacto de "tocarse" ni el umbral de "componente
    # diminuto" ("el implementador decide y documenta"); supuestos
    # documentados en el reporte del sprint, mismo estilo que
    # Check:DefaultCloseGapRatio/DefaultDuplicatePointRatio (M1-S08):
    # component_default_touch_ratio (0.1% de la diagonal del SVG) es más
    # estricto que la tolerancia de "casi cerrado" de M1-S08 (0.5%) a
    # propósito -- "tocarse" acá decide si dos piezas se fusionan en una sola
    # (una decisión de mayor impacto que solo avisar de un posible defecto),
    # así que el umbral es más conservador. component_default_tiny_area_ratio
    # (0.05% del área total del bounding box de la capa) NO filtra
    # componentes diminutos (ver app.core.component_analysis): se reportan
    # igual, marcados `is_tiny`, porque en el dominio de corte láser una
    # pieza real -aunque chica- nunca debería desaparecer en silencio.
    # component_timeout_seconds es un presupuesto interno del proceso Python
    # (mismo criterio que check_timeout_seconds): tanto la detección de
    # contención como la de contacto son O(n^2) puro Python, se acotan con un
    # hilo separado. max_component_subpaths acota la cantidad de subpaths
    # analizables antes de intentar ambos análisis O(n^2).
    component_timeout_seconds: int = 15
    max_component_subpaths: int = 20_000
    component_default_touch_ratio: float = 0.001
    component_default_tiny_area_ratio: float = 0.0005
    component_min_touch_ratio: float = 0.0
    component_max_touch_ratio: float = 0.5
    component_min_tiny_area_ratio: float = 0.0
    component_max_tiny_area_ratio: float = 0.5

    # Salvaguarda de rendimiento: si la imagen tiene más colores únicos que
    # esto (fotografías/degradés de tono continuo, no el caso de uso
    # principal de esta herramienta -- logos/diseños gráficos para corte
    # láser), se re-cuantiza el color a menos niveles por canal antes de
    # clusterizar, para acotar el costo O(k) del armado de clusters y el
    # O(k^2) de la fusión hacia `max_colors` -- ver
    # app.core.color_palette_pipeline.extract_unique_colors.
    max_palette_unique_colors: int = 512


@lru_cache
def get_settings() -> Settings:
    return Settings()
