namespace Vectify.Api.Checking;

/// <summary>
/// Un problema geométrico detectado por el Laser Checker de paths abiertos/
/// duplicados (M1-S08) -- ver app.core.path_checker del lado Python, que es
/// donde se calcula el análisis en sí (esta clase solo tipa lo que ya
/// devolvió Python, ya validado defensivamente por
/// <see cref="Clients.PythonCheckClient"/>). Siempre de solo lectura: ningún
/// issue lleva una acción de "corregir", solo dónde está (path_index/
/// subpath_index/bounds/puntos) y qué tan grave es (severity).
/// </summary>
public abstract record CheckIssue
{
    private CheckIssue()
    {
    }

    /// <summary>
    /// Un subpath sin comando `Z` cuyo primer y último punto están dentro de
    /// CheckParameters.CloseGapRatio. Severidad siempre "error": un path
    /// abierto que debería cerrarse produce un corte incompleto.
    /// </summary>
    public sealed record OpenPath(
        string Id,
        string Severity,
        int PathIndex,
        int SubpathIndex,
        (double X, double Y) StartPoint,
        (double X, double Y) EndPoint,
        double GapDistance,
        CheckBounds Bounds) : CheckIssue;

    /// <summary>
    /// Un grupo de 2+ subpaths geométricamente iguales o casi-iguales dentro
    /// de CheckParameters.DuplicatePointRatio. <see cref="Exact"/> distingue
    /// un duplicado byte-a-byte (severidad "error") de uno casi-idéntico
    /// (severidad "warning").
    /// </summary>
    public sealed record DuplicatePath(
        string Id,
        string Severity,
        bool Exact,
        double MaxPointDistance,
        IReadOnlyList<CheckDuplicateMember> Members) : CheckIssue;
}

/// <summary>Un subpath miembro de un grupo de duplicados -- ver CheckIssue.DuplicatePath.</summary>
public sealed record CheckDuplicateMember(int PathIndex, int SubpathIndex, CheckBounds Bounds);
