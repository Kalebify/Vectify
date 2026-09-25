namespace Vectify.Api.Export;

/// <summary>
/// M1-S10 cierra el primer flujo productivo dejando elegir CUALQUIERA de los
/// tres artefactos SVG ya persistidos por el pipeline como fuente a exportar:
/// una VectorVersion (M1-S05), una SimplificationVersion (M1-S07) o una
/// DimensionVersion (M1-S09) -- extiende a un tercer valor el mismo patrón
/// dual ya usado en Checking/Dimensioning (M1-S08/M1-S09). El caller indica
/// cuál vía <see cref="ExportSourceKind"/> + el ID correspondiente.
/// </summary>
public enum ExportSourceKind
{
    Vector,
    Simplification,
    Dimension,
}
