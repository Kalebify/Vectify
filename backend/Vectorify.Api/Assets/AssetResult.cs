using Vectorify.Api.Data;

namespace Vectorify.Api.Assets;

/// <summary>
/// Resultado compartido por las operaciones de <see cref="IAssetService"/> -- mismo patrón de
/// result type discriminado que <see cref="Vectorify.Api.ProjectManagement.ProjectResult"/>
/// (M2.2-S03), para que <c>AssetEndpoints</c> traduzca cada caso a un código HTTP sin
/// excepciones de control de flujo.
/// </summary>
public abstract record AssetResult
{
    private AssetResult()
    {
    }

    /// <summary>Un Asset recién subido, el endpoint responde 201.</summary>
    public sealed record Ready(Asset Record) : AssetResult;

    /// <summary>Contenido binario listo para servir (GET de descarga), el endpoint responde 200 en streaming.</summary>
    public sealed record Downloaded(Stream Content, string ContentType, string FileName) : AssetResult;

    /// <summary>Hard delete aplicado (storage + fila), el endpoint responde 204 sin cuerpo.</summary>
    public sealed record Deleted : AssetResult;

    /// <summary>
    /// No existe un Asset con ese Id para ese Project, O el Project no existe, O existe pero
    /// pertenece a otro usuario -- mismo caso para los tres (el endpoint responde 404, nunca
    /// 403: no revela que el recurso existe a un usuario no autorizado, mismo criterio que
    /// M2.2-S03).
    /// </summary>
    public sealed record NotFound(string Code, string Message) : AssetResult;

    /// <summary>Archivo ausente/vacío/demasiado grande/formato no soportado, o 'type' inválido (el endpoint responde 400).</summary>
    public sealed record ValidationFailed(string Code, string Message) : AssetResult;

    /// <summary>Fallo de IFileStorage al guardar o borrar (el endpoint responde 500, ver spec.md "storage_failure").</summary>
    public sealed record StorageFailed(string Code, string Message) : AssetResult;
}
