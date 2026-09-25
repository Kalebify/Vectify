namespace Vectify.Api.Checking;

/// <summary>
/// Resultado de correr el Laser Checker (M1-S08). Nunca hay un caso de
/// "cache-hit"/versión nueva como en Simplification/Vectorization -- este
/// análisis es de SOLO LECTURA y no persiste nada (ver spec.md, Definition
/// of Done: "sin modificar el SVG"), así que no hace falta un registro
/// versionado: <see cref="Ready"/> es siempre el resultado directo de la
/// llamada a Python, recalculado en cada request.
/// </summary>
public abstract record CheckResult
{
    private CheckResult()
    {
    }

    public sealed record Ready(
        Guid SourceId,
        CheckSourceKind SourceKind,
        IReadOnlyList<CheckIssue> Issues,
        int SkippedPathCount,
        CheckParameters Parameters) : CheckResult;

    /// <summary>No existe un proyecto/imagen con esos IDs, o el sourceId de origen no existe para esa imagen (el endpoint responde 404).</summary>
    public sealed record NotFound(string Code, string Message) : CheckResult;

    /// <summary>Los parámetros están fuera de rango o faltan (el endpoint responde 400).</summary>
    public sealed record ValidationFailed(string Code, string Message) : CheckResult;

    /// <summary>El motor Python (o el storage) falló de una forma controlada (el endpoint mapea el código HTTP correspondiente).</summary>
    public sealed record UpstreamError(string Code, string Message) : CheckResult;
}
