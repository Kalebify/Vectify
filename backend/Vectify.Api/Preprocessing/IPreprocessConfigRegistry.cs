namespace Vectify.Api.Preprocessing;

/// <summary>
/// Historial de configuraciones de preprocesamiento por imagen: versiona cada
/// configuración guardada y permite referenciar/cachear el preview generado
/// para una combinación de parámetros ya vista (ver spec.md M1-S03, sección
/// "ASP.NET Core Web API": "cachear/referenciar preview; mantener versionado
/// de configuración").
/// </summary>
public interface IPreprocessConfigRegistry
{
    /// <summary>Busca un preview ya generado para exactamente estos parámetros (cache hit).</summary>
    PreprocessConfigRecord? FindByParams(Guid projectId, Guid imageId, PreprocessParameters parameters);

    /// <summary>Última configuración guardada para la imagen, o null si nunca se generó un preview.</summary>
    PreprocessConfigRecord? FindLatest(Guid projectId, Guid imageId);

    /// <summary>Busca un registro por su previewId, para servir los bytes del preview.</summary>
    PreprocessConfigRecord? FindByPreviewId(Guid projectId, Guid imageId, Guid previewId);

    /// <summary>
    /// Próximo número de versión para esta imagen (empieza en 1 y crece
    /// monótonamente con cada configuración nueva, ver <see cref="Save"/>).
    /// </summary>
    int NextVersion(Guid projectId, Guid imageId);

    /// <summary>Guarda (o sobrescribe) un registro, indexado por proyecto/imagen, parámetros y previewId.</summary>
    void Save(PreprocessConfigRecord record);
}
