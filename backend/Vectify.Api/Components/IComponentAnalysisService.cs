namespace Vectify.Api.Components;

/// <summary>
/// Orquesta el análisis de componentes físicos independientes de UNA capa
/// vectorial (M2-S03, ver "Ambigüedades detectadas" de spec.md: el cálculo
/// geométrico corre en Python reutilizando la infraestructura compartida de
/// M1-S08, esta capa fina en ASP.NET Core solo localiza el SVG de origen,
/// llama a Python y persiste el resultado versionado). Cada capa ya ES una
/// <see cref="Vectify.Api.Vectorization.VectorVersion"/> normal (M2-S02),
/// así que este servicio localiza el SVG de origen vía
/// <see cref="Vectify.Api.Vectorization.IVectorizationService"/> -- no
/// necesita saber nada de paletas/grupos de color, es indistinguible de
/// analizar componentes sobre cualquier otro VectorVersion.
/// </summary>
public interface IComponentAnalysisService
{
    /// <summary>
    /// Calcula (o reutiliza desde caché, si ya se calculó para exactamente
    /// este VectorId -- inmutable una vez generado) el análisis de
    /// componentes físicos de la capa. Precondición: debe existir una
    /// VectorVersion con ese VectorId para esta imagen -- si no, devuelve
    /// un error explícito sin llamar a Python.
    /// </summary>
    Task<ComponentSetResult> AnalyzeAsync(Guid projectId, Guid imageId, Guid vectorId, CancellationToken cancellationToken);

    /// <summary>Recupera el último análisis de componentes calculado para este VectorId, o null si nunca se calculó uno.</summary>
    ComponentSetVersion? FindLatest(Guid projectId, Guid imageId, Guid vectorId);
}
