using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vectify.Api.Options;
using Vectify.Api.Projects;
using Vectify.Api.Tests.TestSupport;
using Vectify.Api.Validation;

namespace Vectify.Api.Tests.Projects;

/// <summary>
/// Pruebas unitarias de ProjectUploadService: orquesta validación + storage +
/// registro sin depender de HTTP real (eso lo cubre EndToEnd/ProjectEndpointsTests).
/// Cubre el camino feliz, el fallo de storage y la idempotencia por header.
/// </summary>
public sealed class ProjectUploadServiceTests
{
    private static FormFile CreateFile(byte[] bytes, string fileName, string contentType) =>
        new(new MemoryStream(bytes), 0, bytes.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType,
        };

    private static ProjectUploadService CreateService(FakeFileStorage storage, InMemoryProjectRegistry registry)
    {
        var validator = new ImageUploadValidator(Microsoft.Extensions.Options.Options.Create(new UploadOptions()));
        return new ProjectUploadService(validator, storage, registry, NullLogger<ProjectUploadService>.Instance);
    }

    [Fact]
    public async Task UploadAsync_WhenFileIsValid_CreatesProjectAndStoresOriginal()
    {
        var storage = new FakeFileStorage();
        var registry = new InMemoryProjectRegistry();
        var service = CreateService(storage, registry);
        var file = CreateFile(SampleImages.ValidPng1x1, "logo.png", "image/png");

        var result = await service.UploadAsync(file, idempotencyKey: null, CancellationToken.None);

        var created = Assert.IsType<ProjectUploadResult.Created>(result);
        Assert.Equal("logo.png", created.Response.Filename);
        Assert.Equal("image/png", created.Response.MimeType);
        Assert.Equal(SampleImages.ValidPng1x1.Length, created.Response.Bytes);
        Assert.Equal(1, created.Response.Width);
        Assert.Equal(1, created.Response.Height);
        Assert.Equal("uploaded", created.Response.Status);
        Assert.NotEqual(Guid.Empty, created.Response.ProjectId);
        Assert.NotEqual(Guid.Empty, created.Response.ImageId);
        Assert.Single(storage.Saved);

        var record = registry.Find(created.Response.ProjectId, created.Response.ImageId);
        Assert.NotNull(record);
    }

    [Fact]
    public async Task UploadAsync_WhenFileIsInvalid_ReturnsValidationFailedWithoutTouchingStorage()
    {
        var storage = new FakeFileStorage();
        var registry = new InMemoryProjectRegistry();
        var service = CreateService(storage, registry);
        var file = CreateFile([], "vacio.png", "image/png");

        var result = await service.UploadAsync(file, idempotencyKey: null, CancellationToken.None);

        var failed = Assert.IsType<ProjectUploadResult.ValidationFailed>(result);
        Assert.Equal("empty_file", failed.Code);
        Assert.Equal(0, storage.SaveCallCount);
    }

    [Fact]
    public async Task UploadAsync_WhenStorageFails_ReturnsStorageFailed()
    {
        var storage = new FakeFileStorage { ThrowOnSave = true };
        var registry = new InMemoryProjectRegistry();
        var service = CreateService(storage, registry);
        var file = CreateFile(SampleImages.ValidPng1x1, "logo.png", "image/png");

        var result = await service.UploadAsync(file, idempotencyKey: null, CancellationToken.None);

        Assert.IsType<ProjectUploadResult.StorageFailed>(result);
    }

    [Fact]
    public async Task UploadAsync_WhenIdempotencyKeyIsReplayed_ReturnsExistingProjectWithoutSavingAgain()
    {
        var storage = new FakeFileStorage();
        var registry = new InMemoryProjectRegistry();
        var service = CreateService(storage, registry);
        var file = CreateFile(SampleImages.ValidPng1x1, "logo.png", "image/png");

        var first = await service.UploadAsync(file, idempotencyKey: "abc-123", CancellationToken.None);
        var created = Assert.IsType<ProjectUploadResult.Created>(first);

        var second = await service.UploadAsync(
            CreateFile(SampleImages.ValidPng1x1, "logo.png", "image/png"),
            idempotencyKey: "abc-123",
            CancellationToken.None);

        var replayed = Assert.IsType<ProjectUploadResult.Replayed>(second);
        Assert.Equal(created.Response.ProjectId, replayed.Response.ProjectId);
        Assert.Equal(created.Response.ImageId, replayed.Response.ImageId);
        Assert.Equal(1, storage.SaveCallCount); // no se guardó una segunda vez
    }
}
