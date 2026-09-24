using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Vectify.Api.Contracts;
using Vectify.Api.Endpoints;
using Vectify.Api.Tests.TestSupport;

namespace Vectify.Api.Tests.EndToEnd;

/// <summary>
/// Pruebas de integración HTTP de POST /api/v1/projects y GET .../original: levantan
/// la Web API real (WebApplicationFactory) con almacenamiento local apuntando a un
/// directorio temporal. Cubre carga válida, los errores controlados del spec y la
/// recuperación del original.
/// </summary>
public sealed class ProjectEndpointsTests : IDisposable
{
    private readonly string _storageRoot = Path.Combine(Path.GetTempPath(), "vectify-endpoint-tests-" + Guid.NewGuid().ToString("n"));

    [Fact]
    public async Task PostProjects_WhenImageIsValid_CreatesProjectAndOriginalIsRecoverable()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        using var content = MultipartWith(SampleImages.ValidPng1x1, "logo.png", "image/png");
        var response = await client.PostAsync("/api/v1/projects", content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UploadImageResponse>();
        Assert.NotNull(body);
        Assert.Equal("logo.png", body!.Filename);
        Assert.Equal("image/png", body.MimeType);
        Assert.Equal(SampleImages.ValidPng1x1.Length, body.Bytes);
        Assert.Equal(1, body.Width);
        Assert.Equal(1, body.Height);
        Assert.Equal("uploaded", body.Status);
        Assert.NotNull(response.Headers.Location);

        var original = await client.GetAsync(response.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, original.StatusCode);
        var originalBytes = await original.Content.ReadAsByteArrayAsync();
        Assert.Equal(SampleImages.ValidPng1x1, originalBytes);
        Assert.Equal("image/png", original.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task PostProjects_WhenFormatIsNotSupported_ReturnsBadRequestWithControlledError()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        using var content = MultipartWith(SampleImages.NotAnImage, "archivo.gif", "image/gif");
        var response = await client.PostAsync("/api/v1/projects", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.Equal("unsupported_format", error!.Code);
    }

    [Fact]
    public async Task PostProjects_WhenFileIsEmpty_ReturnsBadRequestWithEmptyFile()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        using var content = MultipartWith([], "vacio.png", "image/png");
        var response = await client.PostAsync("/api/v1/projects", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.Equal("empty_file", error!.Code);
    }

    [Fact]
    public async Task PostProjects_WhenFileIsCorrupt_ReturnsBadRequestWithCorruptFile()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        using var content = MultipartWith(SampleImages.NotAnImage, "logo.png", "image/png");
        var response = await client.PostAsync("/api/v1/projects", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.Equal("corrupt_file", error!.Code);
    }

    [Fact]
    public async Task PostProjects_WhenFileExceedsMaxSize_ReturnsBadRequestWithFileTooLarge()
    {
        await using var factory = CreateFactory(maxFileSizeBytes: 10);
        var client = factory.CreateClient();

        using var content = MultipartWith(SampleImages.ValidPng1x1, "logo.png", "image/png");
        var response = await client.PostAsync("/api/v1/projects", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.Equal("file_too_large", error!.Code);
    }

    [Fact]
    public async Task PostProjects_WhenFormHasNoFileField_ReturnsBadRequestWithEmptyFile()
    {
        // Formulario multipart bien formado (parseable) pero sin el campo "file":
        // el escenario realista de "el usuario confirmó sin elegir ningún archivo".
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        using var content = new MultipartFormDataContent { { new StringContent("sin-archivo"), "note" } };
        var response = await client.PostAsync("/api/v1/projects", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.Equal("empty_file", error!.Code);
    }

    [Fact]
    public async Task PostProjects_WhenMultipartBodyIsMalformed_ReturnsBadRequestWithUploadInterrupted()
    {
        // Content-Type multipart/form-data pero cuerpo totalmente vacío: no parseable.
        // Se trata como carga interrumpida en vez de dejar una excepción sin manejar.
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        using var content = new MultipartFormDataContent();
        var response = await client.PostAsync("/api/v1/projects", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.Equal("upload_interrupted", error!.Code);
    }

    [Fact]
    public async Task PostProjects_WhenIdempotencyKeyIsRepeated_ReturnsSameProjectWithOkInsteadOfCreated()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();
        const string idempotencyKey = "retry-abc";

        using var firstContent = MultipartWith(SampleImages.ValidPng1x1, "logo.png", "image/png");
        firstContent.Headers.Add(ProjectEndpoints.IdempotencyHeaderName, idempotencyKey);
        var firstResponse = await client.PostAsync("/api/v1/projects", firstContent);
        var firstBody = await firstResponse.Content.ReadFromJsonAsync<UploadImageResponse>();

        using var secondContent = MultipartWith(SampleImages.ValidPng1x1, "logo.png", "image/png");
        secondContent.Headers.Add(ProjectEndpoints.IdempotencyHeaderName, idempotencyKey);
        var secondResponse = await client.PostAsync("/api/v1/projects", secondContent);
        var secondBody = await secondResponse.Content.ReadFromJsonAsync<UploadImageResponse>();

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.Equal(firstBody!.ProjectId, secondBody!.ProjectId);
        Assert.Equal(firstBody.ImageId, secondBody.ImageId);
    }

    [Fact]
    public async Task GetOriginal_WhenProjectDoesNotExist_ReturnsNotFound()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/v1/projects/{Guid.NewGuid()}/images/{Guid.NewGuid()}/original");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static MultipartFormDataContent MultipartWith(byte[] bytes, string fileName, string contentType)
    {
        var multipart = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        multipart.Add(fileContent, "file", fileName);
        return multipart;
    }

    private WebApplicationFactory<Program> CreateFactory(long maxFileSizeBytes = 15 * 1024 * 1024)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["PythonEngine:BaseUrl"] = "http://127.0.0.1:1",
                    ["Cors:AllowedOrigins"] = "http://localhost:5173",
                    ["Storage:RootPath"] = _storageRoot,
                    ["Upload:MaxFileSizeBytes"] = maxFileSizeBytes.ToString(),
                });
            });
        });
    }

    public void Dispose()
    {
        if (Directory.Exists(_storageRoot))
        {
            Directory.Delete(_storageRoot, recursive: true);
        }
    }
}
