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


class EmptyMaskError(PreprocessingError):
    """La máscara recibida no tiene ningún píxel de foreground (blanco): no hay
    nada que vectorizar. Ver spec.md M1-S05, "Pruebas": "máscara vacía (sin
    contenido) -- debe manejarse como caso controlado, no como crash". Se
    decidió tratarla como error accionable (422) en vez de devolver un SVG
    vacío "exitoso" -- ver reporte del sprint, "Decisiones de diseño"."""

    code = "empty_mask"


class VectorizationTimeoutError(PreprocessingError):
    """El motor de trazado (ver app.core.vector_engine.VectorEngine) tardó más
    que Vectorize:TimeoutSeconds y se abortó. VTracer es una llamada nativa
    (Rust) sin mecanismo de cancelación cooperativa; el timeout se aplica
    desde afuera con un hilo separado (best effort: el hilo de trazado puede
    seguir corriendo en background tras reportar el timeout) -- ver reporte
    del sprint, "Excepciones/limitaciones"."""

    code = "vectorization_timeout"


class VectorizationEngineError(PreprocessingError):
    """El motor de trazado falló de una forma no contemplada por los errores
    de arriba (excepción nativa inesperada). Nunca debería filtrar detalles
    específicos del motor (VTracer) fuera de app.core.vector_engine -- ver
    spec.md M1-S05, Definition of Done."""

    code = "vectorization_engine_error"


class InvalidSvgError(PreprocessingError):
    """El SVG crudo devuelto por el motor de trazado no es XML válido o no
    tiene un elemento <svg> raíz. No debería ocurrir con VTracer (trazado
    geométrico puro) pero se valida de todos modos como defensa en
    profundidad antes de sanitizar/persistir -- ver spec.md M1-S05,
    "Seguridad/robustez"."""

    code = "invalid_svg"


class SvgOutputTooLargeError(PreprocessingError):
    """El SVG sanitizado resultante supera Vectorize:MaxSvgOutputBytes. Límite
    de tamaño de salida explícito, ver spec.md M1-S05, "Seguridad/robustez":
    "límites de ejecución/tamaño"."""

    code = "svg_output_too_large"
