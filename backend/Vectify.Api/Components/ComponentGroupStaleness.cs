namespace Vectify.Api.Components;

/// <summary>
/// Calcula, de forma puramente defensiva y SIN lanzar excepciones, qué
/// <see cref="ComponentGroup.ComponentIds"/> de un grupo ya no existen en la
/// <see cref="ComponentSetVersion"/> vigente para su VectorId. Un
/// <see cref="ComponentGroup"/> es válido PARA SIEMPRE contra la versión de
/// componentes con la que se creó (identificada por
/// <see cref="ComponentGroup.ComponentSetId"/>) -- ver spec.md, "Ambigüedades
/// detectadas": no se migra automáticamente. Esta clase solo sirve para que
/// la capa de presentación (endpoint) pueda avisar, sin crashear, si el
/// componente ya no aparece en el análisis más reciente (p. ej. si en algún
/// momento futuro M2-S03 recalculara bajo el mismo VectorId, o si el grupo
/// quedó huérfano por cualquier otro motivo).
/// </summary>
public static class ComponentGroupStaleness
{
    /// <summary>
    /// True si <paramref name="currentComponents"/> es null (nunca se calculó
    /// -- o ya no existe -- un análisis de componentes para el VectorId de
    /// este grupo), o si el ComponentSetId del grupo ya no coincide con el
    /// vigente, o si falta alguno de sus componentIds en la versión vigente.
    /// </summary>
    public static bool IsStale(ComponentGroup group, ComponentSetVersion? currentComponents) =>
        currentComponents is null
        || currentComponents.ComponentSetId != group.ComponentSetId
        || MissingComponentIds(group, currentComponents).Count > 0;

    /// <summary>IDs del grupo que no aparecen entre los componentes de <paramref name="currentComponents"/> (lista vacía si no hay ninguno, o si currentComponents es null).</summary>
    public static IReadOnlyList<string> MissingComponentIds(ComponentGroup group, ComponentSetVersion? currentComponents)
    {
        if (currentComponents is null)
        {
            return group.ComponentIds;
        }

        var currentIds = currentComponents.Components.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        return group.ComponentIds.Where(id => !currentIds.Contains(id)).ToList();
    }
}
