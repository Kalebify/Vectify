namespace Vectify.Api.Dimensioning;

/// <summary>
/// M1-S09 no aclara si "dimensiones reales en mm" se aplica sobre una
/// VectorVersion (M1-S05) o también sobre una SimplificationVersion (M1-S07)
/// si el usuario ya simplificó -- mismo criterio ya usado en M1-S08
/// (<see cref="Vectify.Api.Checking.CheckSourceKind"/>): se aceptan AMBOS
/// orígenes, el caller indica cuál vía <see cref="DimensionSourceKind"/> + el
/// ID correspondiente.
/// </summary>
public enum DimensionSourceKind
{
    Vector,
    Simplification,
}
