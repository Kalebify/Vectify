using Vectify.Api.Contracts;

namespace Vectify.Api.Projects;

/// <summary>Resultado de intentar crear un proyecto a partir de una imagen subida.</summary>
public abstract record ProjectUploadResult
{
    private ProjectUploadResult()
    {
    }

    /// <summary>Proyecto nuevo creado (el endpoint responde 201 Created).</summary>
    public sealed record Created(UploadImageResponse Response) : ProjectUploadResult;

    /// <summary>
    /// Replay de una carga previa identificada por Idempotency-Key: no se creó un
    /// proyecto nuevo (el endpoint responde 200 OK con el proyecto existente).
    /// </summary>
    public sealed record Replayed(UploadImageResponse Response) : ProjectUploadResult;

    /// <summary>Validación falló antes de tocar el storage (el endpoint responde 400).</summary>
    public sealed record ValidationFailed(string Code, string Message) : ProjectUploadResult;

    /// <summary>La capa de almacenamiento falló (el endpoint responde 500).</summary>
    public sealed record StorageFailed(string Message) : ProjectUploadResult;
}
