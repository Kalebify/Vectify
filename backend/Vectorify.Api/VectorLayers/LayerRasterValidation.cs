namespace Vectorify.Api.VectorLayers;

/// <summary>
/// Resultado de la validación raster-vs-vector de UNA capa (M2.1-S03): el
/// motor Python, al generar el SVG de esta capa (ver
/// <see cref="Vectorify.Api.Clients.IPythonVectorLayerClient"/>), rasteriza el
/// resultado de vuelta al mismo tamaño que la máscara de origen y lo compara
/// contra (a) esa misma máscara -- fidelidad propia -- y (b) la UNIÓN de las
/// máscaras de las DEMÁS capas de la misma paleta -- contaminación cruzada
/// entre colores (la señal "crucial" pedida por spec.md: "evitar que
/// regiones de otro color aparezcan dentro del layer seleccionado").
///
/// El CÁLCULO ocurre íntegramente en Python (ver
/// app.core.raster_validation.compare_layer_raster) -- este registro solo
/// transporta y persiste el resultado ya calculado junto a la
/// <see cref="VectorLayer"/> correspondiente, sin recalcular nada del lado
/// de .NET (mismo criterio de "no duplicar el cálculo" que
/// <see cref="VectorLayer.VectorId"/> respecto a
/// <see cref="Vectorify.Api.Components.ComponentSetVersion"/>).
///
/// Análisis de SOLO LECTURA, nunca bloqueante: una discrepancia por encima
/// de tolerancia se loggea como advertencia (ver
/// <see cref="VectorLayerService"/>) pero NUNCA impide que la capa se genere
/// ni se persista -- decisión documentada en el reporte del sprint (spec.md,
/// "Ambigüedades detectadas": "advertir, no bloquear").
/// </summary>
public sealed record LayerRasterValidation(
    double OwnMismatchRatio,
    double OwnMismatchTolerance,
    bool OwnMismatchWithinTolerance,
    double ContaminationRatio,
    double ContaminationTolerance,
    bool ContaminationWithinTolerance,
    IReadOnlyList<string> Warnings)
{
    /// <summary>True si AMBAS métricas están dentro de tolerancia -- atajo usado para decidir si loggear una advertencia.</summary>
    public bool IsWithinTolerance => OwnMismatchWithinTolerance && ContaminationWithinTolerance;
}
