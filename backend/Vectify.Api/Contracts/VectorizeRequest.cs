namespace Vectify.Api.Contracts;

/// <summary>
/// Cuerpo JSON de POST /api/v1/projects/{projectId}/images/{imageId}/vectorize.
/// MaskId referencia la máscara B/N YA generada (M1-S04) sobre la que se
/// vectoriza -- la vectorización es la etapa siguiente del mismo pipeline,
/// nunca opera sobre el preview preprocesado ni el original. Sin parámetros
/// ajustables en este sprint (ver Vectify.Api.Vectorization.VectorParameters).
/// </summary>
public sealed record VectorizeRequest(Guid MaskId);
