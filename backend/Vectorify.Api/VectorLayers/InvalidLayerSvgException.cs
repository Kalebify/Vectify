namespace Vectorify.Api.VectorLayers;

/// <summary>
/// El SVG de una capa (ya recibido de Python, ver <see cref="IPythonVectorLayerClient"/>)
/// no es XML válido o no tiene un elemento &lt;svg&gt; como raíz -- defensa en
/// profundidad adicional dentro de <see cref="SvgFillWriter"/>, análoga a
/// <see cref="Vectorify.Api.Dimensioning.InvalidDimensionSourceSvgException"/>
/// de M1-S09. No debería ocurrir en la práctica: <c>PythonVectorLayerClient</c>
/// ya valida que cada SVG devuelto sea XML bien formado con raíz &lt;svg&gt;
/// antes de llegar acá.
/// </summary>
public sealed class InvalidLayerSvgException : Exception
{
    public InvalidLayerSvgException(string message) : base(message)
    {
    }

    public InvalidLayerSvgException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
