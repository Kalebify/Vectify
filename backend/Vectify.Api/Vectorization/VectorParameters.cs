namespace Vectify.Api.Vectorization;

/// <summary>
/// Parámetros efectivos de la etapa de vectorización. En este sprint
/// (M1-S05) no hay ningún parámetro ajustable expuesto a React -- el motor de
/// trazado (VTracer, encapsulado detrás de app.core.vector_engine.VectorEngine
/// del lado Python) corre con una configuración fija y determinista. Se
/// mantiene como tipo propio (en vez de omitirlo) para seguir el mismo patrón
/// arquitectónico que ThresholdParameters/PreprocessParameters -- caché y
/// versionado por (máscara de origen + parámetros efectivos) -- y para no
/// tener que cambiar la forma de IVectorVersionRegistry el día que este
/// sprint agregue algún parámetro configurable (ej. selección de motor o
/// preset de calidad, ambos fuera de alcance de M1-S05).
/// </summary>
public sealed record VectorParameters
{
    /// <summary>
    /// Clave estable para deduplicar/cachear vectorizaciones. Constante por
    /// ahora (no hay parámetros que varíen); el caller (VectorizationService)
    /// combina esto con el maskId de origen, igual que ThresholdParameters.
    /// </summary>
    public string ToCacheKey() => "engine=vtracer";
}
