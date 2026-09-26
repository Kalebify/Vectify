namespace Vectify.Api.Components;

/// <summary>
/// Agrupación LÓGICA de componentes físicos (M2-S05): referencia una lista
/// de <see cref="LayerComponent.Id"/> ya calculados por M2-S03, más la
/// identidad exacta (<see cref="ComponentSetId"/>) de la
/// <see cref="ComponentSetVersion"/> que estaba vigente cuando se creó el
/// grupo. NO contiene paths ni geometría propia -- agrupar nunca modifica,
/// funde ni reduce las piezas físicas reales, solo les da un nombre y una
/// referencia en común para operarlas juntas (seleccionar como conjunto en
/// el árbol/canvas). Un componente puede pertenecer a más de un grupo a la
/// vez (ver spec.md, "Ambigüedades detectadas": no se prohíbe explícitamente
/// y restringirlo agregaría una validación no pedida).
///
/// <see cref="ComponentSetId"/> es la clave de "versión de componentes que
/// existía al crear el grupo": como <see cref="ComponentSetVersion"/> es
/// inmutable una vez calculada para un VectorId (M2-S03 solo cachea, nunca
/// recalcula un mismo VectorId), referenciarla por su Id alcanza para nunca
/// migrar automáticamente un grupo viejo a una versión de componentes
/// distinta -- ver <see cref="ComponentGroupService"/>.
/// </summary>
public sealed record ComponentGroup(
    Guid GroupId,
    string Name,
    IReadOnlyList<string> ComponentIds,
    Guid ComponentSetId,
    DateTimeOffset CreatedAt);
