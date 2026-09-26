using Vectify.Api.Contracts;

namespace Vectify.Api.PhysicalUnion;

/// <summary>
/// Orquesta la unión física de piezas (M2-S06): selección de 2+
/// componentes YA calculados por M2-S03, dentro de una misma capa/VectorId
/// -- distinta y explícitamente separada de <see cref="Vectify.Api.Components.IComponentGroupService"/>
/// (M2-S05, lógica, nunca toca geometría). Preview calcula la geometría
/// real SIN persistir nada; confirm repite el mismo cómputo y, solo si es
/// geométricamente posible, persiste una <see cref="Vectify.Api.Vectorization.VectorVersion"/>
/// nueva (la anterior nunca se destruye) más un registro de auditoría.
/// </summary>
public interface IPhysicalUnionService
{
    Task<PhysicalUnionPreviewResult> PreviewAsync(
        Guid projectId, Guid imageId, Guid vectorId, PhysicalUnionRequest request, CancellationToken cancellationToken);

    Task<PhysicalUnionConfirmResult> ConfirmAsync(
        Guid projectId, Guid imageId, Guid vectorId, PhysicalUnionRequest request, CancellationToken cancellationToken);

    /// <summary>Última unión física CONFIRMADA a partir de este VectorId de origen, o null si nunca se confirmó una.</summary>
    PhysicalUnionVersion? FindLatest(Guid projectId, Guid imageId, Guid vectorId);
}
