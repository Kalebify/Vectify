namespace Vectify.Api.Contracts;

/// <summary>
/// Cuerpo JSON de POST .../simplify/preview y .../simplify/apply. VectorId
/// referencia el SVG YA vectorizado (M1-S05) sobre el que se simplifica --
/// mismo criterio que VectorizeRequest.MaskId/ThresholdRequest.PreviewId
/// (el ID del recurso de origen viaja en el body, no en la URL). Preset
/// ("low"/"medium"/"high", case-insensitive) y Tolerance (valor numérico
/// custom de epsilon_ratio) son mutuamente excluyentes: exactamente uno de
/// los dos debe venir informado (ver SimplificationParameterValidator) --
/// spec.md, criterios de aceptación: "validar tolerancia ... no solo el
/// preset sino también si se expone un valor numérico custom".
/// </summary>
public sealed record SimplifyRequest(Guid VectorId, string? Preset, double? Tolerance);
