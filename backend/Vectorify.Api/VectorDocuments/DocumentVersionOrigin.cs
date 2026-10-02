namespace Vectorify.Api.VectorDocuments;

/// <summary>
/// Vocabulario CERRADO de <see cref="Data.DocumentVersion.Origin"/> (M2.2-S06) -- no un enum
/// de Postgres (la columna sigue siendo texto libre), validado/convertido a nivel de
/// aplicación en C#, mismo criterio exacto que
/// <see cref="Vectorify.Api.ManufacturingOperations.ManufacturingOperationKind"/> (ver
/// <see cref="DocumentVersionOriginParser"/> para la conversión hacia/desde el valor de wire
/// persistido). De estos 5 valores, hoy solo dos se producen realmente:
/// <see cref="ManualEdit"/> (<see cref="VectorDocumentService.SaveAsync"/>, reemplaza el
/// string libre <c>"workspace_save"</c> que emitía M2.2-S05) y <see cref="Restore"/>
/// (<see cref="VectorDocumentService.RestoreAsync"/>, nuevo en esta tarjeta).
/// <see cref="Vectorize"/>/<see cref="Autosave"/> quedan reservados (ningún flujo de
/// "primera versión automática desde vectorización clásica" ni autosave existe todavía) y
/// <see cref="AiEdit"/> reservado para IA futura -- ningún código de este sprint los emite.
/// </summary>
public enum DocumentVersionOrigin
{
    Vectorize,
    ManualEdit,
    Autosave,
    Restore,
    AiEdit,
}

/// <summary>
/// Convierte <see cref="DocumentVersionOrigin"/> hacia/desde el valor de texto persistido en
/// <see cref="Data.DocumentVersion.Origin"/> (<c>VECTORIZE</c>/<c>MANUAL_EDIT</c>/
/// <c>AUTOSAVE</c>/<c>RESTORE</c>/<c>AI_EDIT</c>) -- usado por el
/// <c>HasConversion</c> de <see cref="Data.VectorizationDbContext.OnModelCreating"/>. A
/// diferencia de <see cref="Vectorify.Api.ManufacturingOperations.ManufacturingOperationParser"/>,
/// no hace falta un método que tolere valores de wire externos: <see cref="Data.DocumentVersion.Origin"/>
/// nunca viaja como input de un request HTTP (ningún endpoint de esta tarjeta lo acepta como
/// parámetro -- siempre lo decide el servidor), así que <see cref="Parse"/> lanza ante un
/// valor desconocido en vez de devolver <c>null</c> silenciosamente (solo puede pasar si la
/// fila fue escrita por fuera de este backend).
/// </summary>
public static class DocumentVersionOriginParser
{
    public static string ToWireValue(DocumentVersionOrigin origin) => origin switch
    {
        DocumentVersionOrigin.Vectorize => "VECTORIZE",
        DocumentVersionOrigin.ManualEdit => "MANUAL_EDIT",
        DocumentVersionOrigin.Autosave => "AUTOSAVE",
        DocumentVersionOrigin.Restore => "RESTORE",
        DocumentVersionOrigin.AiEdit => "AI_EDIT",
        _ => throw new ArgumentOutOfRangeException(nameof(origin), origin, "Valor de DocumentVersionOrigin desconocido."),
    };

    public static DocumentVersionOrigin Parse(string value) => value switch
    {
        "VECTORIZE" => DocumentVersionOrigin.Vectorize,
        "MANUAL_EDIT" => DocumentVersionOrigin.ManualEdit,
        "AUTOSAVE" => DocumentVersionOrigin.Autosave,
        "RESTORE" => DocumentVersionOrigin.Restore,
        "AI_EDIT" => DocumentVersionOrigin.AiEdit,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, $"Origin '{value}' desconocido."),
    };
}
