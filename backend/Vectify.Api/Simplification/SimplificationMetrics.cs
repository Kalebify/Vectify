using Vectify.Api.Vectorization;

namespace Vectify.Api.Simplification;

/// <summary>
/// Nodos antes/después y % de reducción de la etapa de simplificación,
/// calculados por el motor Python (ver spec.md M1-S07, criterios de
/// aceptación: "Reducción de nodos es medible y reportada (nodeCount antes,
/// nodeCount después, % reducción)"). Reutiliza <see cref="VectorMetrics"/>
/// (mismo par path_count/approx_node_count/bounds que ya devuelve la
/// vectorización, M1-S05) en vez de duplicar su forma -- "antes" y "después"
/// son ambos snapshots completos del mismo tipo de estadística.
/// </summary>
public sealed record SimplificationMetrics(VectorMetrics Before, VectorMetrics After, double ReductionPercent);
