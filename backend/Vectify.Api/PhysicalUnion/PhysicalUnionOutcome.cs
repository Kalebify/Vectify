using Vectify.Api.Vectorization;

namespace Vectify.Api.PhysicalUnion;

/// <summary>
/// Resultado geométrico YA calculado (por Python, ver
/// Clients.IPythonPhysicalUnionClient) de fusionar 2+ componentes
/// seleccionados: el SVG resultante y sus métricas, más el detalle de la
/// validación post-operación no negociable (spec.md: "nunca fingir unión")
/// -- `ComponentCountAfter` SIEMPRE es exactamente
/// `ComponentCountBefore - ComponentIds.Count + 1` en este punto: si no lo
/// fuera, Python ya rechazó la operación con un error explícito ANTES de
/// que este tipo pudiera construirse (ver
/// app.core.physical_union.PhysicalUnionImpossibleError). Compartido entre
/// preview (no persiste nada) y confirm (persiste `Svg` como una
/// VectorVersion nueva) -- ver PhysicalUnionService.
/// </summary>
public sealed record PhysicalUnionOutcome(
    string Svg,
    string ContentType,
    int Width,
    int Height,
    VectorMetrics Metrics,
    int ComponentCountBefore,
    int ComponentCountAfter,
    string Strategy,
    int BridgeCount);
