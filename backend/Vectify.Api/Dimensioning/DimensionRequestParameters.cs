namespace Vectify.Api.Dimensioning;

/// <summary>
/// Forma "cruda" de la solicitud de dimensiones YA validada
/// estructuralmente (sourceId presente, sourceKind conocido, la combinación
/// ancho/alto es coherente con <see cref="LockAspectRatio"/>, y cada valor
/// informado está en rango) pero TODAVÍA sin resolver contra el tamaño en
/// píxeles del SVG de origen -- a diferencia de
/// Simplification.SimplificationParameters/Checking.CheckParameters, esta
/// etapa necesita el aspect ratio del SVG de origen (que solo se conoce
/// después de localizarlo) para calcular el valor faltante cuando la
/// proporción está bloqueada, así que la validación ocurre en dos pasos --
/// ver <see cref="IDimensionParameterValidator"/>.
/// </summary>
public sealed record DimensionRequestParameters(
    DimensionSourceKind SourceKind, double? WidthMm, double? HeightMm, bool LockAspectRatio);
