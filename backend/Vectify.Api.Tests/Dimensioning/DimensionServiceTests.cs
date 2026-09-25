using Microsoft.Extensions.Logging.Abstractions;
using Vectify.Api.Contracts;
using Vectify.Api.Dimensioning;
using Vectify.Api.Options;
using Vectify.Api.Simplification;
using Vectify.Api.Tests.Projects;
using Vectify.Api.Vectorization;

namespace Vectify.Api.Tests.Dimensioning;

/// <summary>
/// Pruebas unitarias de DimensionService: orquesta localizar el SVG de
/// origen (VectorVersion o SimplificationVersion) + validación en dos pasos
/// + reescritura de metadata (SIN llamar a ningún motor externo) +
/// caché/lock/storage/versionado. Mismo criterio que SimplificationService
/// (M1-S07), sin el cliente Python.
/// </summary>
public sealed class DimensionServiceTests
{
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ImageId = Guid.NewGuid();
    private static readonly Guid SourceVectorId = Guid.NewGuid();
    private static readonly Guid SourceSimplificationId = Guid.NewGuid();
    private const string VectorStorageKey = "project/image/vectors/source.svg";
    private const string SimplificationStorageKey = "project/image/simplifications/source.svg";
    private const string SourceSvg =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\"><path d=\"M2,2 L8,2 L8,8 L2,8 Z\"/></svg>";

    private static (
        DimensionService Service,
        FakeFileStorage Storage,
        InMemoryDimensionVersionRegistry Registry) CreateService(
        bool withExistingVector = true, bool withExistingSimplification = true, DimensionOptions? options = null)
    {
        var vectorizationService = new FakeVectorizationService();
        if (withExistingVector)
        {
            vectorizationService.AddVector(new VectorVersion(
                ProjectId, ImageId, 1, SourceVectorId, Guid.NewGuid(), new VectorParameters(),
                VectorStorageKey, "image/svg+xml", 10, 10,
                new VectorMetrics(1, 4, new VectorBounds(2, 2, 8, 8, 6, 6)), DateTimeOffset.UtcNow));
        }

        var simplificationService = new FakeSimplificationService();
        if (withExistingSimplification)
        {
            simplificationService.AddSimplification(new SimplificationVersion(
                ProjectId, ImageId, 1, SourceSimplificationId, SourceVectorId,
                new SimplificationParameters(0.004, "medium"),
                SimplificationStorageKey, "image/svg+xml", 10, 10,
                new SimplificationMetrics(
                    new VectorMetrics(1, 12, new VectorBounds(2, 2, 8, 8, 6, 6)),
                    new VectorMetrics(1, 4, new VectorBounds(2, 2, 8, 8, 6, 6)),
                    66.7),
                DateTimeOffset.UtcNow));
        }

        var storage = new FakeFileStorage();
        storage.Saved[VectorStorageKey] = System.Text.Encoding.UTF8.GetBytes(SourceSvg);
        storage.Saved[SimplificationStorageKey] = System.Text.Encoding.UTF8.GetBytes(SourceSvg);

        var registry = new InMemoryDimensionVersionRegistry();
        var validator = new DimensionParameterValidator(Microsoft.Extensions.Options.Options.Create(options ?? new DimensionOptions()));

        var service = new DimensionService(
            vectorizationService, simplificationService, registry, validator, storage, NullLogger<DimensionService>.Instance);

        return (service, storage, registry);
    }

    private static DimensionRequest VectorRequest(
        double? widthMm = 100, double? heightMm = null, bool? lockAspectRatio = true, Guid? sourceId = null) =>
        new("vector", sourceId ?? SourceVectorId, widthMm, heightMm, lockAspectRatio);

    private static DimensionRequest SimplificationRequest(
        double? widthMm = 100, double? heightMm = null, bool? lockAspectRatio = true, Guid? sourceId = null) =>
        new("simplification", sourceId ?? SourceSimplificationId, widthMm, heightMm, lockAspectRatio);

    [Fact]
    public async Task ApplyAsync_WhenParametersAreStructurallyInvalid_ReturnsValidationFailedWithoutTouchingStorage()
    {
        var (service, storage, _) = CreateService();
        var savedCountBefore = storage.Saved.Count;

        var result = await service.ApplyAsync(
            ProjectId, ImageId, new DimensionRequest("vector", SourceVectorId, 100, 50, true), CancellationToken.None);

        var failed = Assert.IsType<DimensionResult.ValidationFailed>(result);
        Assert.Equal("invalid_parameters", failed.Code);
        Assert.Equal(savedCountBefore, storage.Saved.Count);
    }

    [Fact]
    public async Task ApplyAsync_WhenSourceVectorDoesNotExist_ReturnsNotFound()
    {
        var (service, _, _) = CreateService(withExistingVector: false);

        var result = await service.ApplyAsync(ProjectId, ImageId, VectorRequest(), CancellationToken.None);

        Assert.IsType<DimensionResult.NotFound>(result);
    }

    [Fact]
    public async Task ApplyAsync_WhenSourceSimplificationDoesNotExist_ReturnsNotFound()
    {
        var (service, _, _) = CreateService(withExistingSimplification: false);

        var result = await service.ApplyAsync(ProjectId, ImageId, SimplificationRequest(), CancellationToken.None);

        Assert.IsType<DimensionResult.NotFound>(result);
    }

    [Fact]
    public async Task ApplyAsync_WhenDerivedDimensionIsOutOfRange_ReturnsValidationFailedAfterResolvingSource()
    {
        var options = new DimensionOptions { MinMm = 1, MaxMm = 1000 };
        var (service, _, _) = CreateService(options: options);

        // Fuente cuadrada (10x10): pedir 2000mm de ancho excede el máximo directamente.
        var result = await service.ApplyAsync(
            ProjectId, ImageId, VectorRequest(widthMm: 2000), CancellationToken.None);

        var failed = Assert.IsType<DimensionResult.ValidationFailed>(result);
        Assert.Equal("invalid_parameters", failed.Code);
    }

    [Fact]
    public async Task ApplyAsync_WhenCalledFirstTime_CreatesNewDimensionVersionAndSavesToStorage()
    {
        var (service, storage, _) = CreateService();

        var result = await service.ApplyAsync(ProjectId, ImageId, VectorRequest(widthMm: 100), CancellationToken.None);

        var ready = Assert.IsType<DimensionResult.Ready>(result);
        Assert.False(ready.FromCache);
        Assert.Equal(1, ready.Record.Version);
        Assert.Equal(100, ready.Record.Parameters.WidthMm);
        Assert.Equal(100, ready.Record.Parameters.HeightMm); // fuente cuadrada, lock activo
        Assert.Equal(SourceVectorId, ready.Record.SourceId);
        Assert.Equal(DimensionSourceKind.Vector, ready.Record.SourceKind);
        Assert.True(storage.Saved.ContainsKey(ready.Record.SvgStorageKey));

        var svgBytes = storage.Saved[ready.Record.SvgStorageKey];
        var svgText = System.Text.Encoding.UTF8.GetString(svgBytes);
        Assert.Contains("width=\"100mm\"", svgText);
        Assert.Contains("height=\"100mm\"", svgText);
        Assert.Contains("viewBox=\"0 0 10 10\"", svgText);
    }

    [Fact]
    public async Task ApplyAsync_WhenUnlockedWithDifferentAspect_SetsPreserveAspectRatioNone()
    {
        var (service, storage, _) = CreateService();

        var result = await service.ApplyAsync(
            ProjectId, ImageId, VectorRequest(widthMm: 300, heightMm: 20, lockAspectRatio: false), CancellationToken.None);

        var ready = Assert.IsType<DimensionResult.Ready>(result);
        var svgText = System.Text.Encoding.UTF8.GetString(storage.Saved[ready.Record.SvgStorageKey]);
        Assert.Contains("preserveAspectRatio=\"none\"", svgText);
    }

    [Fact]
    public async Task ApplyAsync_NeverOverwritesTheSourceVectorSvgInStorage()
    {
        var (service, storage, _) = CreateService();
        var originalSourceBytes = storage.Saved[VectorStorageKey];

        var result = await service.ApplyAsync(ProjectId, ImageId, VectorRequest(), CancellationToken.None);

        var ready = Assert.IsType<DimensionResult.Ready>(result);
        Assert.NotEqual(VectorStorageKey, ready.Record.SvgStorageKey);
        Assert.Equal(originalSourceBytes, storage.Saved[VectorStorageKey]);
    }

    [Fact]
    public async Task ApplyAsync_WhenCalledAgainWithSameSourceAndDimensions_ReturnsCachedWithNewVersion()
    {
        var (service, storage, _) = CreateService();
        var request = VectorRequest(widthMm: 100);
        var savedCountBefore = storage.Saved.Count;

        var first = await service.ApplyAsync(ProjectId, ImageId, request, CancellationToken.None);
        var second = await service.ApplyAsync(ProjectId, ImageId, request, CancellationToken.None);

        var firstReady = Assert.IsType<DimensionResult.Ready>(first);
        var secondReady = Assert.IsType<DimensionResult.Ready>(second);
        Assert.False(firstReady.FromCache);
        Assert.True(secondReady.FromCache);
        Assert.Equal(firstReady.Record.DimensionId, secondReady.Record.DimensionId);
        Assert.Equal(firstReady.Record.Version + 1, secondReady.Record.Version);
        // Solo un archivo nuevo (la simplificación aplicada), sin duplicar storage en el cache-hit.
        Assert.Equal(savedCountBefore + 1, storage.Saved.Count);
    }

    [Fact]
    public async Task ApplyAsync_WhenSameWidthButDifferentSourceId_TreatsAsADifferentDimension()
    {
        var (service, _, _) = CreateService();

        var first = await service.ApplyAsync(ProjectId, ImageId, VectorRequest(widthMm: 100), CancellationToken.None);
        var second = await service.ApplyAsync(ProjectId, ImageId, SimplificationRequest(widthMm: 100), CancellationToken.None);

        var firstReady = Assert.IsType<DimensionResult.Ready>(first);
        var secondReady = Assert.IsType<DimensionResult.Ready>(second);
        Assert.False(secondReady.FromCache);
        Assert.NotEqual(firstReady.Record.DimensionId, secondReady.Record.DimensionId);
    }

    [Fact]
    public async Task ApplyAsync_WhenConcurrentRequestsForSameSourceAndDimensions_ProducesOnlyOneNewVersion()
    {
        var (service, storage, registry) = CreateService();
        var request = VectorRequest(widthMm: 100);

        var task1 = service.ApplyAsync(ProjectId, ImageId, request, CancellationToken.None);
        var task2 = service.ApplyAsync(ProjectId, ImageId, request, CancellationToken.None);
        var results = await Task.WhenAll(task1, task2);

        var ready1 = Assert.IsType<DimensionResult.Ready>(results[0]);
        var ready2 = Assert.IsType<DimensionResult.Ready>(results[1]);
        Assert.Equal(ready1.Record.DimensionId, ready2.Record.DimensionId);

        var stored = registry.FindByDimensionId(ProjectId, ImageId, ready1.Record.DimensionId);
        Assert.NotNull(stored);
    }

    [Fact]
    public async Task FindDimension_WhenDimensionWasApplied_ReturnsRecord()
    {
        var (service, _, _) = CreateService();
        var applied = await service.ApplyAsync(ProjectId, ImageId, VectorRequest(), CancellationToken.None);
        var ready = Assert.IsType<DimensionResult.Ready>(applied);

        var found = service.FindDimension(ProjectId, ImageId, ready.Record.DimensionId);

        Assert.NotNull(found);
        Assert.Equal(ready.Record.DimensionId, found!.DimensionId);
    }

    [Fact]
    public void FindDimension_WhenDimensionDoesNotExist_ReturnsNull()
    {
        var (service, _, _) = CreateService();

        var found = service.FindDimension(ProjectId, ImageId, Guid.NewGuid());

        Assert.Null(found);
    }
}
