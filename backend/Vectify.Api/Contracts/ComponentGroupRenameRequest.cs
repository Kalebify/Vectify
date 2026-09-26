namespace Vectify.Api.Contracts;

/// <summary>Cuerpo JSON de POST .../groups/{groupId}/rename: renombra un grupo de componentes.</summary>
public sealed record ComponentGroupRenameRequest(string Name);
