namespace Vectify.Api.Components;

/// <summary>
/// Una versión guardada del CONJUNTO de <see cref="ComponentGroup"/> de una
/// capa vectorial (identificada por <see cref="VectorId"/>, igual que
/// <see cref="ComponentSetVersion"/>): agrupar, desagrupar y renombrar crean
/// SIEMPRE una versión nueva de este conjunto, nunca mutan una existente --
/// mismo criterio de historial versionado que el resto del pipeline. Un
/// VectorId solo tiene UN conjunto de grupos evolucionando en el tiempo (no
/// hay múltiples "sesiones" de agrupación en paralelo para la misma capa,
/// a diferencia de ColorPalette/PaletteId), así que VectorId alcanza como
/// clave -- no hace falta un Id de sesión separado.
/// </summary>
public sealed record ComponentGroupSetVersion(
    Guid ProjectId,
    Guid ImageId,
    Guid VectorId,
    int Version,
    IReadOnlyList<ComponentGroup> Groups,
    DateTimeOffset CreatedAt);
