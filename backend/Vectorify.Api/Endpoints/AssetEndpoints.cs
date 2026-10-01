using Vectorify.Api.Assets;
using Vectorify.Api.Contracts;
using Vectorify.Api.Data;

namespace Vectorify.Api.Endpoints;

/// <summary>
/// API NUEVA de gestión de Assets (M2.2-S04), bajo <c>/api/v2/projects/{projectId}/assets</c>
/// -- mismo prefijo de versión que <see cref="ProjectV2Endpoints"/> (M2.2-S03) por
/// consistencia. Conecta el <see cref="Asset"/> persistente de M2.2-S02 con almacenamiento
/// real vía <see cref="Vectorify.Api.Storage.IFileStorage"/> (extendido, no reinventado --
/// ver spec.md, "Hallazgo clave").
///
/// Arquitectura: Endpoint (acá) -&gt; <see cref="IAssetService"/> -&gt;
/// <see cref="Vectorify.Api.Assets.Persistence.IAssetRepository"/> +
/// <see cref="Vectorify.Api.Storage.IFileStorage"/>. Ningún endpoint de acá toca
/// <see cref="Vectorify.Api.Data.VectorizationDbContext"/> ni <c>IFileStorage</c>
/// directamente.
/// </summary>
public static class AssetEndpoints
{
    private const string FileFieldName = "file";
    private const string TypeFieldName = "type";

    public static void MapAssetEndpoints(this IEndpointRouteBuilder app)
    {
        // multipart/form-data con un campo "file" (el binario) y un campo "type"
        // (Asset.Type, p. ej. "original"/"preview"/"vector"/"export"). El formulario se lee
        // manualmente (en vez de dejar que el binding automático de IFormFile lo haga) --
        // mismo criterio que POST /api/v1/projects (ProjectEndpoints): una carga
        // interrumpida a mitad de subida se traduce a "upload_interrupted" en vez de una
        // excepción sin manejar.
        app.MapPost("/api/v2/projects/{projectId:guid}/assets", async (
            Guid projectId,
            HttpRequest request,
            IAssetService service,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            IFormFile? file;
            string? type;
            try
            {
                if (!request.HasFormContentType)
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        "unsupported_format",
                        "La solicitud debe ser multipart/form-data con un campo 'file' y un campo 'type'."));
                }

                var form = await request.ReadFormAsync(cancellationToken);
                file = form.Files.GetFile(FileFieldName);
                type = form[TypeFieldName];
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // El cliente cortó la conexión de verdad: no hay a quién responderle.
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "La carga del asset se interrumpió o el formulario multipart no se pudo leer");
                return Results.BadRequest(new ApiErrorResponse(
                    "upload_interrupted",
                    "La carga se interrumpió antes de completarse. Intentá de nuevo."));
            }

            var result = await service.UploadAsync(projectId, type, file, cancellationToken);
            return result switch
            {
                AssetResult.Ready ready => Results.Created(
                    $"/api/v2/projects/{projectId}/assets/{ready.Record.Id}", ToResponse(ready.Record)),
                AssetResult.NotFound notFound => Results.NotFound(new ApiErrorResponse(notFound.Code, notFound.Message)),
                AssetResult.ValidationFailed failed => Results.BadRequest(new ApiErrorResponse(failed.Code, failed.Message)),
                AssetResult.StorageFailed storageFailed => StorageFailureResult(storageFailed),
                _ => UnexpectedResult(),
            };
        })
        .WithName("UploadAssetV2")
        .WithTags("AssetsV2")
        .Produces<AssetResponse>(StatusCodes.Status201Created)
        .Produces<ApiErrorResponse>(StatusCodes.Status400BadRequest)
        .Produces<ApiErrorResponse>(StatusCodes.Status404NotFound)
        .WithSummary("Sube un Asset nuevo (original/preview/svg/export) para un proyecto. multipart/form-data con campos 'file' y 'type'.")
        .WithDescription(
            "Ownership vía IUserContext: 404 uniforme si el Project dueño no es del usuario efectivo (nunca 403). " +
            "La clave de storage se deriva de assetId/type/extensión -- nunca del nombre de archivo que mandó el " +
            "usuario (ver spec.md M2.2-S04, 'Seguridad'). Guarda primero en storage y recién después la fila: si " +
            "el storage falla, la fila nunca se crea.");

        app.MapGet("/api/v2/projects/{projectId:guid}/assets/{assetId:guid}", async (
            Guid projectId,
            Guid assetId,
            IAssetService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.DownloadAsync(projectId, assetId, cancellationToken);
            return result switch
            {
                AssetResult.Downloaded downloaded => Results.Stream(downloaded.Content, downloaded.ContentType, downloaded.FileName),
                AssetResult.NotFound notFound => Results.NotFound(new ApiErrorResponse(notFound.Code, notFound.Message)),
                _ => UnexpectedResult(),
            };
        })
        .WithName("DownloadAssetV2")
        .WithTags("AssetsV2")
        .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
        .Produces<ApiErrorResponse>(StatusCodes.Status404NotFound)
        .WithSummary("Descarga el contenido binario de un Asset, en streaming vía IFileStorage.OpenReadAsync.")
        .WithDescription(
            "404 uniforme si el Project no existe/no es del usuario efectivo, si el Asset no existe para ese " +
            "Project, o si la fila existe pero su contenido ya no está en storage (caso límite).");

        app.MapDelete("/api/v2/projects/{projectId:guid}/assets/{assetId:guid}", async (
            Guid projectId,
            Guid assetId,
            IAssetService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.DeleteAsync(projectId, assetId, cancellationToken);
            return result switch
            {
                AssetResult.Deleted => Results.NoContent(),
                AssetResult.NotFound notFound => Results.NotFound(new ApiErrorResponse(notFound.Code, notFound.Message)),
                AssetResult.StorageFailed storageFailed => StorageFailureResult(storageFailed),
                _ => UnexpectedResult(),
            };
        })
        .WithName("DeleteAssetV2")
        .WithTags("AssetsV2")
        .Produces(StatusCodes.Status204NoContent)
        .Produces<ApiErrorResponse>(StatusCodes.Status404NotFound)
        .WithSummary("Hard delete de un Asset: borra el archivo real (IFileStorage.DeleteAsync) y la fila.")
        .WithDescription(
            "Distinto del soft-delete de Project (M2.2-S03), que NUNCA toca Assets. Ownership vía IUserContext, " +
            "mismo criterio 404 que GET/POST.");
    }

    private static IResult StorageFailureResult(AssetResult.StorageFailed storageFailed) => Results.Json(
        new ApiErrorResponse(storageFailed.Code, storageFailed.Message), statusCode: StatusCodes.Status500InternalServerError);

    private static IResult UnexpectedResult() => Results.Json(
        new ApiErrorResponse("internal_error", "Ocurrió un error inesperado al procesar el asset."),
        statusCode: StatusCodes.Status500InternalServerError);

    private static AssetResponse ToResponse(Asset asset) => new(
        asset.Id,
        asset.ProjectId,
        asset.Type,
        asset.MimeType,
        asset.FileName,
        asset.Size,
        asset.Width,
        asset.Height,
        asset.Checksum,
        asset.CreatedAt);
}
