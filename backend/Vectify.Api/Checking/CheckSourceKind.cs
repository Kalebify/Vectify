namespace Vectify.Api.Checking;

/// <summary>
/// M1-S08 no aclara si el checker opera sobre una VectorVersion (M1-S05) o
/// también sobre una SimplificationVersion (M1-S07) si el usuario ya
/// simplificó -- decisión documentada en el reporte del sprint: se aceptan
/// AMBOS orígenes (los dos son SVG con la misma forma de paths M/L/Z), el
/// caller indica cuál vía <see cref="CheckSourceKind"/> + el ID
/// correspondiente.
/// </summary>
public enum CheckSourceKind
{
    Vector,
    Simplification,
}
