namespace Vectify.Api.Checking;

/// <summary>Caja delimitadora de un único subpath (no de todo el SVG) -- suficiente para que React ubique aproximadamente un issue.</summary>
public sealed record CheckBounds(double MinX, double MinY, double MaxX, double MaxY);
