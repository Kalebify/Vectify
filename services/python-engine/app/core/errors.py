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


class InvalidInputSvgError(PreprocessingError):
    """El SVG recibido como ENTRADA de la etapa de simplificación (M1-S07) --
    un SVG ya generado por una vectorización o simplificación previa -- no es
    UTF-8 válido, no es XML bien formado, o no tiene un elemento <svg> como
    raíz. A diferencia de InvalidSvgError (que cubre el SVG CRUDO recién
    generado por el motor de trazado -- un fallo interno del propio proceso,
    500), esto es un problema del INPUT recibido del caller y se trata como
    error de cliente (400), igual criterio que CorruptImageError."""

    code = "invalid_input_svg"


class SvgInputTooLargeError(PreprocessingError):
    """El SVG de entrada de la etapa de simplificación (M1-S07) supera
    Vectorize:MaxSvgOutputBytes (mismo límite que el tamaño de salida de
    vectorización: un SVG de entrada nunca debería ser más grande que el
    límite que ya se le aplicó cuando se generó)."""

    code = "svg_input_too_large"


class SimplificationTimeoutError(PreprocessingError):
    """La simplificación de nodos (ver
    app.services.simplification_service.SimplificationService) tardó más que
    Simplify:TimeoutSeconds/simplify_timeout_seconds y se abortó. Mismo
    criterio que VectorizationTimeoutError: Douglas-Peucker es puro Python, se
    acota con un hilo separado (best effort, ver
    SimplificationService._simplify_with_timeout)."""

    code = "simplification_timeout"


class CheckTimeoutError(PreprocessingError):
    """El Laser Checker de paths abiertos/duplicados (M1-S08, ver
    app.services.path_checker_service.PathCheckerService) tardó más que
    Check:TimeoutSeconds/check_timeout_seconds y se abortó. Mismo criterio que
    SimplificationTimeoutError: la detección de duplicados es O(n^2) sobre la
    cantidad de subpaths analizables (puro Python), se acota con un hilo
    separado (best effort, ver PathCheckerService._check_with_timeout)."""

    code = "check_timeout"


class ColorPaletteTimeoutError(PreprocessingError):
    """La detección/reducción de paleta de colores (M2-S01, ver
    app.services.color_palette_service.ColorPaletteService) tardó más que
    ColorPalette:TimeoutSeconds/color_palette_timeout_seconds y se abortó.
    Mismo criterio que SimplificationTimeoutError/CheckTimeoutError: el
    clustering es puro Python/NumPy, se acota con un hilo separado (best
    effort, ver ColorPaletteService._detect_with_timeout)."""

    code = "color_palette_timeout"


class TooManySubpathsError(PreprocessingError):
    """El SVG de entrada del Laser Checker de paths (M1-S08) tiene más
    subpaths analizables que Check:MaxSubpaths/max_check_subpaths. Salvaguarda
    de rendimiento explícita (además del timeout): la detección de
    duplicados (ver app.core.path_checker._detect_duplicates) compara todos
    los pares de subpaths (O(n^2)), así que un diseño con una cantidad
    excesiva de subpaths se rechaza de forma controlada en vez de arriesgar
    agotar CPU/memoria antes siquiera de llegar al timeout."""

    code = "too_many_subpaths"


class ComponentAnalysisTimeoutError(PreprocessingError):
    """El análisis de componentes físicos independientes por capa (M2-S03,
    ver app.services.component_analysis_service.ComponentAnalysisService)
    tardó más que Component:TimeoutSeconds/component_timeout_seconds y se
    abortó. Mismo criterio que CheckTimeoutError: la detección de
    contención/contacto entre subpaths es O(n^2) sobre la cantidad de
    subpaths analizables (puro Python), se acota con un hilo separado (best
    effort, ver ComponentAnalysisService._analyze_with_timeout)."""

    code = "component_analysis_timeout"


class TooManySubpathsForComponentsError(PreprocessingError):
    """El SVG de entrada del análisis de componentes físicos (M2-S03) tiene
    más subpaths analizables que Component:MaxSubpaths/max_component_subpaths.
    Salvaguarda de rendimiento explícita (además del timeout), mismo
    criterio que TooManySubpathsError: tanto la detección de contención
    (nesting, O(n^2) con un point-in-polygon O(m) por par) como la de
    contacto/"tocarse" (O(n^2) comparaciones de segmento a segmento) escalan
    con el cuadrado de la cantidad de subpaths -- un diseño con una cantidad
    excesiva se rechaza de forma controlada en vez de arriesgar agotar
    CPU/memoria antes de llegar al timeout."""

    code = "too_many_component_subpaths"
