namespace Vectify.Api.Contracts;

/// <summary>
/// Cuerpo JSON de POST /api/v1/projects/{projectId}/images/{imageId}/threshold.
/// PreviewId referencia el preview YA preprocesado (M1-S03) sobre el que se
/// aplica el umbral -- el threshold es la etapa siguiente del mismo pipeline,
/// nunca opera directamente sobre el original crudo. Ver spec.md M1-S04,
/// sección "Contratos": "image/version + threshold config -> mask preview +
/// métricas + parámetros efectivos".
/// </summary>
public sealed record ThresholdRequest(Guid PreviewId, int Value, bool Invert);
