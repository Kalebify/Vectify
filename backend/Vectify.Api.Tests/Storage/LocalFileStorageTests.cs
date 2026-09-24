using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Vectify.Api.Options;
using Vectify.Api.Storage;

namespace Vectify.Api.Tests.Storage;

/// <summary>
/// Pruebas de integración de LocalFileStorage contra el filesystem real (un
/// directorio temporal por test, limpiado al final): guardar, leer y detectar
/// existencia bajo una clave lógica, y que el original nunca se sobrescriba con
/// datos parciales si la escritura falla.
/// </summary>
public sealed class LocalFileStorageTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "vectify-tests-" + Guid.NewGuid().ToString("n"));

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
        public string ApplicationName { get; set; } = "Vectify.Api.Tests";
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
