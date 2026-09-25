using Vectify.Api.Contracts;

namespace Vectify.Api.Checking;

/// <summary>
/// Orquesta el Laser Checker de paths abiertos/duplicados (M1-S08): validar
/// parámetros, localizar el SVG de origen (una VectorVersion YA generada,
/// M1-S05, o una SimplificationVersion YA generada, M1-S07 -- según
/// request.SourceKind) y correr el análisis en el motor Python. Sin caché ni
/// versionado: es de solo lectura, cada llamada vuelve a analizar el SVG
/// desde cero (determinista por construcción: mismos bytes + mismas
/// tolerancias = mismo resultado, no hace falta cachear para garantizarlo).
/// </summary>
public interface ICheckService
{
    Task<CheckResult> AnalyzeAsync(Guid projectId, Guid imageId, CheckRequest request, CancellationToken cancellationToken);
}
