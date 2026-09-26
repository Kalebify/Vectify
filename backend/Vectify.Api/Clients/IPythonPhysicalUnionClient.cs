using Vectify.Api.Components;

namespace Vectify.Api.Clients;

/// <summary>
/// Cliente tipado hacia el endpoint de unión física de piezas (POST
/// /api/v1/components/union) del motor Python/FastAPI (M2-S06). Mismo
/// patrón que IPythonComponentClient: cada etapa del pipeline tiene su
/// propio cliente con su propio timeout.
/// </summary>
public interface IPythonPhysicalUnionClient
{
    /// <summary>
    /// Envía el SVG de la capa de origen YA generada (una VectorVersion
    /// existente) y los componentes YA calculados por M2-S03 que se quieren
    /// fusionar (con sus `members` tal cual, para que Python no tenga que
    /// volver a agrupar subpaths). Nunca lanza excepciones: cualquier falla
    /// (offline, timeout, SVG/selección inválida, geometría
    /// autointersectante, unión geométricamente imposible, respuesta
    /// inválida, error HTTP) se traduce a un
    /// <see cref="PythonPhysicalUnionResult"/> con el estado correspondiente.
    /// </summary>
    Task<PythonPhysicalUnionResult> UnionAsync(
        Stream svgContent,
        string contentType,
        string fileName,
        IReadOnlyList<LayerComponent> selectedComponents,
        CancellationToken cancellationToken = default);
}
