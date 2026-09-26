namespace Vectify.Api.Contracts;

/// <summary>Caja delimitadora de un único subpath o de un componente físico completo -- para que React ubique/dibuje su bounding box.</summary>
public sealed record ComponentBoundsPayload(double MinX, double MinY, double MaxX, double MaxY);

/// <summary>Un subpath miembro de un componente físico -- "role" distingue "solid" de "hole" (agujero interno).</summary>
public sealed record ComponentMemberPayload(int PathIndex, int SubpathIndex, string Role, ComponentBoundsPayload Bounds, double Area);

/// <summary>Un componente físico independiente -- ver Vectify.Api.Components.LayerComponent.</summary>
public sealed record LayerComponentPayload(
    string Id,
    IReadOnlyList<ComponentMemberPayload> Members,
    ComponentBoundsPayload Bounds,
    double Area,
    bool IsTiny);

/// <summary>
/// Respuesta de POST/GET .../vectors/{vectorId}/components (M2-S03): el
/// análisis de componentes físicos de UNA capa vectorial, siempre la última
/// versión vigente calculada para ese VectorId.
/// </summary>
public sealed record ComponentSetResponse(
    Guid ProjectId,
    Guid ImageId,
    Guid ComponentSetId,
    int Version,
    Guid VectorId,
    IReadOnlyList<LayerComponentPayload> Components,
    int SkippedPathCount,
    bool Cached);
