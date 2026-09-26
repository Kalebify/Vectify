namespace Vectify.Api.Components;

/// <summary>Caja delimitadora de un único subpath o de un componente físico completo (no de todo el SVG de la capa).</summary>
public sealed record ComponentBounds(double MinX, double MinY, double MaxX, double MaxY);
