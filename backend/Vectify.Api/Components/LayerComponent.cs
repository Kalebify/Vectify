namespace Vectify.Api.Components;

/// <summary>
/// Un componente físico independiente DENTRO de una capa vectorial de un
/// solo color (M2-S03): un conjunto de subpaths que forman una única pieza
/// física conexa (unidos por contención de agujero, por contacto dentro de
/// tolerancia, o ambos -- ver app.core.component_analysis del lado Python).
/// <see cref="Id"/> es estable DENTRO de una versión (mismo VectorId de
/// origen -> mismos ids, mismo orden), NO necesariamente entre distintas
/// generaciones del mismo layer -- ver spec.md, criterio de aceptación.
/// </summary>
public sealed record LayerComponent(
    string Id,
    IReadOnlyList<ComponentMember> Members,
    ComponentBounds Bounds,
    double Area,
    bool IsTiny);
