using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using Vectorify.Api.Contracts;
using Vectorify.Api.Data;
using Vectorify.Api.Storage;

namespace Vectorify.Api.Tests.EndToEnd;

/// <summary>
/// Pruebas de integración HTTP de la API NUEVA de gestión de Assets (M2.2-S04), bajo
/// /api/v2/projects/{projectId}/assets: levantan la Web API real (WebApplicationFactory)
/// contra PostgreSQL real (Testcontainers) y el storage real (filesystem, un directorio
/// temporal por instancia de factory -- NUNCA mockeado, salvo
/// <see cref="Upload_WhenStorageFails_DoesNotLeaveAnOrphanedAssetRow"/>, que sí necesita un
/// IFileStorage fake que lanza FileStorageException (ver spec.md, "Tests").
/// </summary>
public sealed class AssetEndpointsTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly string _storageRootPath =
        Path.Combine(Path.GetTempPath(), "vectorify-asset-tests-" + Guid.NewGuid().ToString("n"));

    public Task InitializeAsync() => _postgres.StartAsync();

    public async Task DisposeAsync()
    {
        if (Directory.Exists(_storageRootPath))
        {
            Directory.Delete(_storageRootPath, recursive: true);
        }

        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task UploadThenDownloadThenDelete_RoundTripsSuccessfully()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var projectId = await CreateProjectAsync(client);

        var uploadResponse = await UploadAssetAsync(client, projectId, "original", "hello.png", [1, 2, 3, 4], "image/png");
        Assert.Equal(HttpStatusCode.Created, uploadResponse.StatusCode);
        var asset = await uploadResponse.Content.ReadFromJsonAsync<AssetResponse>();
        Assert.NotNull(asset);
        Assert.Equal(projectId, asset!.ProjectId);
        Assert.Equal("original", asset.Type);
        Assert.Equal(4, asset.Size);
        Assert.NotNull(uploadResponse.Headers.Location);

        var downloadResponse = await client.GetAsync($"/api/v2/projects/{projectId}/assets/{asset.Id}");
        Assert.Equal(HttpStatusCode.OK, downloadResponse.StatusCode);
        var downloadedBytes = await downloadResponse.Content.ReadAsByteArrayAsync();
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, downloadedBytes);

        var deleteResponse = await client.DeleteAsync($"/api/v2/projects/{projectId}/assets/{asset.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getAfterDeleteResponse = await client.GetAsync($"/api/v2/projects/{projectId}/assets/{asset.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getAfterDeleteResponse.StatusCode);
    }

    [Fact]
    public async Task Download_WhenAssetDoesNotExist_ReturnsNotFound()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var projectId = await CreateProjectAsync(client);

        var response = await client.GetAsync($"/api/v2/projects/{projectId}/assets/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.Equal("not_found", error!.Code);
    }

    [Fact]
    public async Task Upload_TwoAssetsWithSameUserFileNameButDifferentContent_SavesUnderDistinctKeysNeverColliding()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var projectId = await CreateProjectAsync(client);

        var firstResponse = await UploadAssetAsync(client, projectId, "original", "same-name.png", [1, 1, 1], "image/png");
        var secondResponse = await UploadAssetAsync(client, projectId, "original", "same-name.png", [2, 2, 2, 2], "image/png");

        var firstAsset = await firstResponse.Content.ReadFromJsonAsync<AssetResponse>();
        var secondAsset = await secondResponse.Content.ReadFromJsonAsync<AssetResponse>();

        Assert.NotEqual(firstAsset!.Id, secondAsset!.Id);
        Assert.Equal("same-name.png", firstAsset.FileName);
        Assert.Equal("same-name.png", secondAsset.FileName);
        Assert.NotEqual(firstAsset.Checksum, secondAsset.Checksum);

        var firstDownload = await client.GetAsync($"/api/v2/projects/{projectId}/assets/{firstAsset.Id}");
        var secondDownload = await client.GetAsync($"/api/v2/projects/{projectId}/assets/{secondAsset.Id}");
        Assert.Equal(new byte[] { 1, 1, 1 }, await firstDownload.Content.ReadAsByteArrayAsync());
        Assert.Equal(new byte[] { 2, 2, 2, 2 }, await secondDownload.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Upload_WhenStorageFails_DoesNotLeaveAnOrphanedAssetRow()
    {
        await using var factory = CreateFactory(overrideStorageWithFailingFake: true);
        var client = factory.CreateClient();
        var projectId = await CreateProjectAsync(client);

        var response = await UploadAssetAsync(client, projectId, "original", "wont-be-saved.png", [9, 9, 9], "image/png");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.Equal("storage_failure", error!.Code);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VectorizationDbContext>();
        Assert.False(await dbContext.Assets.AnyAsync(a => a.ProjectId == projectId));
    }

    [Fact]
    public async Task Upload_Checksum_MatchesRealHashOfSavedContent()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var projectId = await CreateProjectAsync(client);
        byte[] content = [10, 20, 30, 40, 50];
        var expectedChecksum = Convert.ToHexStringLower(SHA256.HashData(content));

        var response = await UploadAssetAsync(client, projectId, "original", "checksum.png", content, "image/png");
        var asset = await response.Content.ReadFromJsonAsync<AssetResponse>();

        Assert.Equal(expectedChecksum, asset!.Checksum);
    }

    [Fact]
    public async Task Upload_FileNameWithPathTraversal_NeverAffectsTheRealStorageKey()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var projectId = await CreateProjectAsync(client);

        var response = await UploadAssetAsync(client, projectId, "original", "../../etc/passwd.png", [7, 7], "image/png");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var asset = await response.Content.ReadFromJsonAsync<AssetResponse>();
        Assert.NotNull(asset);

        // FileName se preserva solo como metadata informativa (ya sin el path), pero la
        // clave de storage real se derivó de assetId/type/extensión -- nunca de este valor.
        Assert.Equal("passwd.png", asset!.FileName);

        var expectedPath = Path.Combine(
            _storageRootPath, "assets", "projects", projectId.ToString("N"), "original", $"{asset.Id:N}.png");
        Assert.True(File.Exists(expectedPath));

        var downloadResponse = await client.GetAsync($"/api/v2/projects/{projectId}/assets/{asset.Id}");
        Assert.Equal(HttpStatusCode.OK, downloadResponse.StatusCode);
        Assert.Equal(new byte[] { 7, 7 }, await downloadResponse.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task UploadGetDelete_WhenProjectBelongsToAnotherOwner_AllReturnNotFound()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var foreignProjectId = await SeedProjectForAnotherOwnerAsync(factory, "Proyecto ajeno");

        var uploadResponse = await UploadAssetAsync(client, foreignProjectId, "original", "x.png", [1], "image/png");
        Assert.Equal(HttpStatusCode.NotFound, uploadResponse.StatusCode);

        // Asset insertado directamente en la base (bypass de la API) para poder ejercitar
        // GET/DELETE contra un Asset real de un proyecto ajeno.
        var foreignAssetId = await SeedAssetForProjectAsync(factory, foreignProjectId);

        var getResponse = await client.GetAsync($"/api/v2/projects/{foreignProjectId}/assets/{foreignAssetId}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);

        var deleteResponse = await client.DeleteAsync($"/api/v2/projects/{foreignProjectId}/assets/{foreignAssetId}");
        Assert.Equal(HttpStatusCode.NotFound, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task Upload_WithInvalidType_ReturnsBadRequest()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var projectId = await CreateProjectAsync(client);

        var response = await UploadAssetAsync(client, projectId, "Not Valid!", "x.png", [1], "image/png");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.Equal("invalid_type", error!.Code);
    }

    [Fact]
    public async Task Upload_WithUnsupportedContentType_ReturnsBadRequest()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        var projectId = await CreateProjectAsync(client);

        var response = await UploadAssetAsync(client, projectId, "original", "x.bin", [1, 2, 3], "application/octet-stream");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.Equal("unsupported_format", error!.Code);
    }

    private static async Task<Guid> CreateProjectAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v2/projects", new CreateProjectRequest("Proyecto de assets", null));
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ProjectResponse>();
        return body!.Id;
    }

    private static async Task<HttpResponseMessage> UploadAssetAsync(
        HttpClient client, Guid projectId, string type, string fileName, byte[] content, string contentType)
    {
        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(fileContent, "file", fileName);
        form.Add(new StringContent(type), "type");

        return await client.PostAsync($"/api/v2/projects/{projectId}/assets", form);
    }

    /// <summary>Inserta un Project directamente en la base (bypass de la API) con un OwnerId AJENO al usuario "dev" fijo -- mismo criterio que ProjectV2EndpointsTests.</summary>
    private static async Task<Guid> SeedProjectForAnotherOwnerAsync(WebApplicationFactory<Program> factory, string name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VectorizationDbContext>();

        var stranger = new User
        {
            Id = Guid.NewGuid(),
            DisplayName = "Usuario ajeno",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var project = new Project
        {
            Id = Guid.NewGuid(),
            OwnerId = stranger.Id,
            Name = name,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        dbContext.Users.Add(stranger);
        dbContext.Projects.Add(project);
        await dbContext.SaveChangesAsync();

        return project.Id;
    }

    private static async Task<Guid> SeedAssetForProjectAsync(WebApplicationFactory<Program> factory, Guid projectId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VectorizationDbContext>();

        var assetId = Guid.NewGuid();
        var asset = new Asset
        {
            Id = assetId,
            ProjectId = projectId,
            Type = "original",
            StorageKey = $"projects/{projectId:N}/original/{assetId:N}.png",
            MimeType = "image/png",
            FileName = "ajeno.png",
            Size = 3,
            Checksum = Convert.ToHexStringLower(SHA256.HashData([1, 2, 3])),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        dbContext.Assets.Add(asset);
        await dbContext.SaveChangesAsync();

        return asset.Id;
    }

    private WebApplicationFactory<Program> CreateFactory(bool overrideStorageWithFailingFake = false) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Postgres:ConnectionString"] = _postgres.GetConnectionString(),
                    ["Cors:AllowedOrigins"] = "http://localhost:5173",
                    ["Storage:RootPath"] = Path.Combine(_storageRootPath, "assets"),
                });
            });

            if (overrideStorageWithFailingFake)
            {
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IFileStorage>();
                    services.AddSingleton<IFileStorage>(new FailingFakeFileStorage());
                });
            }
        });

    /// <summary>
    /// IFileStorage que siempre lanza FileStorageException al guardar -- usado ÚNICAMENTE en
    /// <see cref="Upload_WhenStorageFails_DoesNotLeaveAnOrphanedAssetRow"/> (ver spec.md,
    /// "Tests": "un IFileStorage fake/mock que lanza FileStorageException").
    /// </summary>
    private sealed class FailingFakeFileStorage : IFileStorage
    {
        public Task<StoredFile> SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken) =>
            throw new FileStorageException("fallo simulado de storage");

        public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken) =>
            throw new FileNotFoundException(key);

        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task DeleteAsync(string key, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
