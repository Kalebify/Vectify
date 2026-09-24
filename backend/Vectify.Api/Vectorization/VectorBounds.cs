namespace Vectify.Api.Vectorization;

/// <summary>
/// Caja delimitadora (aproximada) del contenido dibujado del SVG resultante,
/// calculada por el motor Python (ver services/python-engine/app/core/
/// svg_processing.py) sobre el marcado YA sanitizado.
/// </summary>
public sealed record VectorBounds(double MinX, double MinY, double MaxX, double MaxY, double Width, double Height);
