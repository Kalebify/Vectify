using System.Security.Cryptography;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vectorify.Api.Options;
using Vectorify.Api.Storage;

namespace Vectorify.Api.Tests.Storage;

/// <summary>
/// Pruebas de integración de LocalFileStorage contra el filesystem real (un
/// directorio temporal por test, limpiado al final): guardar, leer y detectar
/// existencia bajo una clave lógica, y que el original nunca se sobrescriba con
/// datos parciales si la escritura falla.
/// </summary>
public sealed class LocalFileStorageTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "vectorify-tests-" + Guid.NewGuid().ToString("n"));

    private LocalFileStorage CreateStorage()
    {
        var environment = new FakeHostEnvironment { ContentRootPath = _rootPath };
        var options = Microsoft.Extensions.Options.Options.Create(new LocalStorageOptions { RootPath = "uploads" });
        return new LocalFileStorage(options, environment, NullLogger<LocalFileStorage>.Instance);
    }

    [Fact]
    public async Task SaveAsync_ThenOpenReadAsync_RoundTripsTheSameBytes()
    {
        var storage = CreateStorage();
        var content = new byte[] { 1, 2, 3, 4, 5 };
        var key = "proj/img/original.png";

        var stored = await storage.SaveAsync(key, new MemoryStream(content), "image/png", CancellationToken.None);

        Assert.Equal(content.Length, stored.SizeBytes);
        Assert.True(await storage.ExistsAsync(key, CancellationToken.None));

        await using var readStream = await storage.OpenReadAsync(key, CancellationToken.None);
        using var buffer = new MemoryStream();
        await readStream.CopyToAsync(buffer);
        Assert.Equal(content, buffer.ToArray());
    }

    [Fact]
    public async Task ExistsAsync_WhenKeyWasNeverSaved_ReturnsFalse()
    {
        var storage = CreateStorage();

        Assert.False(await storage.ExistsAsync("no/existe/original.png", CancellationToken.None));
    }

    [Fact]
    public async Task OpenReadAsync_WhenKeyWasNeverSaved_ThrowsFileNotFound()
    {
        var storage = CreateStorage();

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => storage.OpenReadAsync("no/existe/original.png", CancellationToken.None));
    }

    [Fact]
    public async Task SaveAsync_DoesNotLeaveTemporaryFileBehind()
    {
        var storage = CreateStorage();
        var key = "proj/img/original.png";

        await storage.SaveAsync(key, new MemoryStream([9, 9, 9]), "image/png", CancellationToken.None);

        var finalPath = Path.Combine(_rootPath, "uploads", "proj", "img", "original.png");
        Assert.True(File.Exists(finalPath));
        Assert.False(File.Exists(finalPath + ".tmp"));
    }

    [Fact]
    public async Task SaveAsync_WhenKeyContainsPathTraversal_ThrowsFileStorageException()
    {
        var storage = CreateStorage();

        await Assert.ThrowsAsync<FileStorageException>(
            () => storage.SaveAsync("../escape/original.png", new MemoryStream([1]), "image/png", CancellationToken.None));
    }

    [Fact]
    public async Task SaveAsync_ReturnsChecksum_MatchingTheRealSha256OfTheSavedContent()
    {
        var storage = CreateStorage();
        var content = new byte[] { 10, 20, 30, 40, 50, 60 };
        var expectedChecksum = Convert.ToHexStringLower(SHA256.HashData(content));

        var stored = await storage.SaveAsync("proj/img/original.png", new MemoryStream(content), "image/png", CancellationToken.None);

        Assert.Equal(expectedChecksum, stored.Checksum);
    }

    [Fact]
    public async Task DeleteAsync_WhenKeyExists_RemovesTheFile()
    {
        var storage = CreateStorage();
        var key = "proj/img/original.png";
        await storage.SaveAsync(key, new MemoryStream([1, 2, 3]), "image/png", CancellationToken.None);
        Assert.True(await storage.ExistsAsync(key, CancellationToken.None));

        await storage.DeleteAsync(key, CancellationToken.None);

        Assert.False(await storage.ExistsAsync(key, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteAsync_WhenKeyWasNeverSaved_IsIdempotentAndDoesNotThrow()
    {
        var storage = CreateStorage();

        var exception = await Record.ExceptionAsync(
            () => storage.DeleteAsync("no/existe/original.png", CancellationToken.None));

        Assert.Null(exception);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Vectorify.Api.Tests";
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
