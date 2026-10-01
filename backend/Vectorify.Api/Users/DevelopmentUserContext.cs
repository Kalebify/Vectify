namespace Vectorify.Api.Users;

/// <summary>
/// Implementación mínima de <see cref="IUserContext"/> para M2.2-S03 (sin Auth real):
/// SIEMPRE devuelve el mismo <see cref="DevelopmentUserId"/> fijo, sin leer headers,
/// cookies ni JWT del request. El <see cref="Data.User"/> correspondiente a ese Id se
/// siembra al ARRANCAR la API (ver <see cref="DevelopmentUserSeeder"/> y Program.cs), no
/// lazily en el primer uso: <see cref="GetEffectiveUserId"/> es deliberadamente síncrono
/// (sin acceso a DB) para mantener la interfaz mínima que pide el spec, así que la fila de
/// Postgres tiene que existir ANTES de que cualquier request intente crear un
/// <see cref="Data.Project"/> con este Id como OwnerId (la FK Project.OwnerId -&gt; Users.Id
/// es NOT NULL + Restrict, ver VectorizationDbContext) -- sembrar al arrancar es más simple
/// y confiable que condicionar cada acceso a un chequeo de existencia.
///
/// Esto hace que el filtrado por OwnerId sea REAL (no un no-op): todo proyecto creado en
/// esta sesión de desarrollo pertenece a ESTE usuario fijo, y los tests pueden crear
/// usuarios adicionales directamente contra la DB para probar el caso "proyecto ajeno" de
/// verdad (ver Vectorify.Api.Tests). <c>M2.2-S09</c> reemplaza/extiende esta
/// implementación (su propio nombre lo sugiere) sin que ProjectService/IProjectRepository
/// necesiten cambiar, porque dependen únicamente de <see cref="IUserContext"/>.
/// </summary>
public sealed class DevelopmentUserContext : IUserContext
{
    /// <summary>
    /// Id fijo y determinístico (no generado en runtime) del usuario "dev" -- estable
    /// entre reinicios del proceso y referenciable directamente desde tests.
    /// </summary>
    public static readonly Guid DevelopmentUserId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public Guid GetEffectiveUserId() => DevelopmentUserId;
}
