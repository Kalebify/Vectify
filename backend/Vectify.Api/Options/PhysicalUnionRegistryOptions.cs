namespace Vectify.Api.Options;

/// <summary>
/// Configuración de la persistencia en disco del historial de operaciones
/// de unión física (<see cref="Vectify.Api.PhysicalUnion.PersistentPhysicalUnionVersionRegistry"/>).
/// Se enlaza desde la sección "PhysicalUnionRegistry". Mismo patrón que
/// <see cref="ComponentGroupRegistryOptions"/>.
/// </summary>
public sealed class PhysicalUnionRegistryOptions
{
    public const string SectionName = "PhysicalUnionRegistry";

    /// <summary>
    /// Carpeta donde se guarda un sidecar JSON por cada VectorId de origen
    /// sobre el que se confirmó al menos una unión física (App_Data/
    /// physical-unions/{ProjectId}/{ImageId}/{SourceVectorId}.json por
    /// default). Relativa a ContentRootPath salvo que sea una ruta absoluta.
    /// </summary>
    public string RootPath { get; set; } = "App_Data/physical-unions";
}
