namespace Vectify.Api.PhysicalUnion;

/// <summary>
/// Historial de operaciones de unión física CONFIRMADAS, versionado por
/// VectorId de ORIGEN (la capa sobre la que se pidió la unión) -- mismo
/// patrón exacto que <see cref="Vectify.Api.Components.IComponentGroupVersionRegistry"/>.
/// </summary>
public interface IPhysicalUnionVersionRegistry
{
    /// <summary>Última unión física confirmada a partir de este VectorId de origen, o null si nunca se confirmó una.</summary>
    PhysicalUnionVersion? FindLatest(Guid projectId, Guid imageId, Guid sourceVectorId);

    /// <summary>Próximo número de versión para este VectorId de origen (empieza en 1, crece monótonamente).</summary>
    int NextVersion(Guid projectId, Guid imageId, Guid sourceVectorId);

    /// <summary>Guarda (o sobrescribe) un registro, indexado por proyecto/imagen/VectorId de origen.</summary>
    void Save(PhysicalUnionVersion record);
}
