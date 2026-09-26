namespace Vectify.Api.PhysicalUnion;

/// <summary>
/// Registro de auditoría/historial de UNA unión física CONFIRMADA (M2-S06):
/// qué componentIds se fusionaron, a partir de qué VectorId de origen
/// (<see cref="SourceVectorId"/> -- la capa/vector sobre la que el usuario
/// pidió la unión), y a qué <see cref="Vectify.Api.Vectorization.VectorVersion.VectorId"/>
/// nuevo quedó el resultado (<see cref="ResultVectorId"/>). A diferencia de
/// <see cref="Vectify.Api.Components.ComponentGroup"/> (M2-S05, edición de
/// metadata pura), esta unión SÍ modificó geometría real -- el SVG nuevo ya
/// se persistió como una <see cref="Vectify.Api.Vectorization.VectorVersion"/>
/// normal, REUTILIZANDO el tipo existente (no uno paralelo, ver spec.md);
/// este registro es SOLO el historial/auditoría de la operación en sí
/// (qué se unió, con qué estrategia, cuándo), versionado por
/// <see cref="SourceVectorId"/> con el mismo patrón exacto que
/// <see cref="Vectify.Api.Components.ComponentGroupSetVersion"/>. La
/// VectorVersion ANTERIOR (la que tenía <see cref="SourceVectorId"/>) nunca
/// se destruye ni se muta -- sigue existiendo en el historial de
/// VectorVersion de la imagen, recuperable como cualquier otra versión.
/// </summary>
public sealed record PhysicalUnionVersion(
    Guid ProjectId,
    Guid ImageId,
    int Version,
    Guid SourceVectorId,
    IReadOnlyList<string> ComponentIds,
    Guid ResultVectorId,
    int ComponentCountBefore,
    int ResultComponentCount,
    string Strategy,
    int BridgeCount,
    DateTimeOffset CreatedAt);
