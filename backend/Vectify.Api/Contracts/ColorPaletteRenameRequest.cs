namespace Vectify.Api.Contracts;

/// <summary>Cuerpo JSON de POST .../color-palette/{paletteId}/rename: renombra un grupo.</summary>
public sealed record ColorPaletteRenameRequest(Guid GroupId, string Name);
