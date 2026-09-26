namespace Vectify.Api.Components;

/// <summary>
/// Un subpath miembro de un <see cref="LayerComponent"/> -- ver
/// app.core.component_analysis (Python), que es donde se calcula el
/// análisis en sí (esta clase solo tipa lo que ya devolvió Python, ya
/// validado defensivamente por <see cref="Clients.PythonComponentClient"/>).
/// <see cref="Role"/> distingue "solid" (suma al área neta del componente)
/// de "hole" (agujero interno -- pertenece al MISMO componente que lo
/// contiene, nunca es un componente aparte, ver spec.md M2-S03, criterio de
/// aceptación).
/// </summary>
public sealed record ComponentMember(int PathIndex, int SubpathIndex, string Role, ComponentBounds Bounds, double Area);
