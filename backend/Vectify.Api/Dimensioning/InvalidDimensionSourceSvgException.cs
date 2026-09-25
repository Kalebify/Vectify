namespace Vectify.Api.Dimensioning;

/// <summary>
/// El SVG de origen (ya guardado en storage por una etapa anterior del
/// pipeline, en teoría siempre bien formado) no se pudo parsear como XML
/// válido con un elemento &lt;svg&gt; como raíz -- defensa en profundidad de
/// <see cref="SvgDimensionWriter"/>, mismo criterio que la validación
/// adicional de PythonVectorizeClient/PythonSimplifyClient: Vectify.Api
/// nunca asume ciegamente que el contenido de storage sigue siendo válido.
/// </summary>
public sealed class InvalidDimensionSourceSvgException : Exception
{
    public InvalidDimensionSourceSvgException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
