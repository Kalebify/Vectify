using Vectify.Api.Storage;

namespace Vectify.Api.Tests.Projects;

/// <summary>IFileStorage en memoria para tests unitarios de ProjectUploadService.</summary>
internal sealed class FakeFileStorage : IFileStorage
{
    public Dictionary<string, byte[]> Saved { get; } = new();
    public int SaveCallCount { get; private set; }
    public bool ThrowOnSave { get; set; }

    public async Task<StoredFile> SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken)
    {
        SaveCallCount++;
        if (ThrowOnSave)
        {
            throw new FileStorageException("fallo simulado de storage");
        }

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();
        Saved[key] = bytes;
        return new StoredFile(key, bytes.Length);
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken)
    {
        if (!Saved.TryGetValue(key, out var bytes))
        {
            throw new FileNotFoundException(key);
        }

        return Task.FromResult<Stream>(new MemoryStream(bytes));
    }

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken) =>
        Task.FromResult(Saved.ContainsKey(key));
}
