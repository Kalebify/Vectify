"""Errores controlados del pipeline de preprocesamiento. Cada subclase lleva un
`code` estable (igual convención que Vectify.Api.Contracts.ApiErrorResponse: el
frontend/backend mapean por código, no por el texto de `message`). Se traducen a
respuestas JSON `{code, message}` por los exception handlers registrados en
app.main.
"""


class PreprocessingError(Exception):
    """Base de todos los errores controlados de preprocesamiento."""

    code = "processing_error"


class CorruptImageError(PreprocessingError):
    """La imagen no se pudo decodificar: bytes corruptos, vacíos o formato no
    soportado por OpenCV."""

    code = "corrupt_image"


class DimensionsExceededError(PreprocessingError):
    """La imagen decodificada supera los límites de ancho/alto/píxeles totales
    configurados (protección contra "decompression bombs" y agotamiento de
    memoria)."""

    code = "dimensions_exceeded"


class InvalidParametersError(PreprocessingError):
    """Los parámetros recibidos no cumplen el esquema/rangos esperados. Defensa
    en profundidad: Vectify.Api ya valida rangos antes de llamar a este servicio,
    pero el motor Python nunca confía ciegamente en su caller."""

    code = "invalid_parameters"
