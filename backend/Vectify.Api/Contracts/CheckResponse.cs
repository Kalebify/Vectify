using System.Text.Json.Serialization;

namespace Vectify.Api.Contracts;

/// <summary>Caja delimitadora de un único subpath, para que React pueda ubicar aproximadamente un issue.</summary>
public sealed record CheckBoundsPayload(double MinX, double MinY, double MaxX, double MaxY);

public sealed record CheckSummaryPayload(int OpenPathCount, int DuplicateGroupCount);

public sealed record CheckDuplicateMemberPayload(int PathIndex, int SubpathIndex, CheckBoundsPayload Bounds);

/// <summary>
/// Base polimórfica de un issue del Laser Checker: React discrimina por el
/// campo JSON "type" ("open_path" | "duplicate_path"), serializado acá vía
/// [JsonPolymorphic]/[JsonDerivedType] (System.Text.Json, soportado desde
/// .NET 7) en vez de aplanar ambos tipos en un único record con campos
/// nullable -- mantiene el contrato de cada issue con solo los campos que le
/// aplican.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(OpenPathIssuePayload), "open_path")]
[JsonDerivedType(typeof(DuplicatePathIssuePayload), "duplicate_path")]
public abstract record CheckIssuePayload(string Id, string Severity);

/// <summary>Un subpath sin `Z` cuyo primer y último punto están dentro de la tolerancia -- severidad siempre "error".</summary>
public sealed record OpenPathIssuePayload(
    string Id,
    string Severity,
    int PathIndex,
    int SubpathIndex,
    double StartX,
    double StartY,
    double EndX,
    double EndY,
    double GapDistance,
    CheckBoundsPayload Bounds) : CheckIssuePayload(Id, Severity);

/// <summary>Un grupo de 2+ subpaths iguales o casi-iguales -- "exact" distingue duplicado byte-a-byte (severidad "error") de casi-idéntico ("warning").</summary>
public sealed record DuplicatePathIssuePayload(
    string Id,
    string Severity,
    bool Exact,
    double MaxPointDistance,
    IReadOnlyList<CheckDuplicateMemberPayload> Members) : CheckIssuePayload(Id, Severity);

/// <summary>
/// Respuesta de POST .../check. Análisis de SOLO LECTURA: nunca incluye el
/// SVG (ni modificado ni sin modificar) -- React ya tiene el SVG que pidió
/// analizar, esta respuesta solo agrega los issues encontrados sobre él.
/// </summary>
public sealed record CheckResponse(
    Guid ProjectId,
    Guid ImageId,
    string SourceKind,
    Guid SourceId,
    CheckSummaryPayload Summary,
    IReadOnlyList<CheckIssuePayload> Issues,
    int SkippedPathCount,
    double CloseGapRatio,
    double DuplicatePointRatio);
