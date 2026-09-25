using Microsoft.Extensions.Logging.Abstractions;
using Vectify.Api.Dimensioning;
using Vectify.Api.Export;
using Vectify.Api.Projects;
using Vectify.Api.Simplification;
using Vectify.Api.Vectorization;

namespace Vectify.Api.Tests.Export;

/// <summary>
/// Pruebas unitarias de ExportService: orquesta localizar el SVG de origen
/// (VectorVersion, SimplificationVersion o DimensionVersion, según
/// sourceKind) + derivar el nombre de archivo, SIN caché/lock/persistencia
/// propia (a diferencia de las etapas que sí generan un artefacto nuevo) --
/// ver spec.md M1-S10, Definition of Done: "corresponde exactamente a una
/// versión del proyecto".
/// </summary>
public sealed class ExportServiceTests
{
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ImageId = Guid.NewGuid();
    private static readonly Guid VectorId = Guid.NewGuid();
    private static readonly Guid SimplificationId = Guid.NewGuid();
    private static readonly Guid DimensionId = Guid.NewGuid();
    private const string VectorStorageKey = "project/image/vectors/vector.svg";
    private const string SimplificationStorageKey = "project/image/simplifications/simplification.svg";
    private const string DimensionStorageKey = "project/image/dimensions/dimension.svg";

    private static (
        ExportService Service,
        FakeVectorizationService VectorizationService,
        FakeSimplificationService SimplificationService,
        FakeDimensionService DimensionService,
        FakeProjectRegistry ProjectRegistry) CreateService(string? fileName = "diseño final.png")
    {
        var vectorizationService = new FakeVectorizationService();
        vectorizationService.AddVector(new VectorVersion(
            ProjectId, ImageId, 2, VectorId, Guid.NewGuid(), new VectorParameters(),
            VectorStorageKey, "image/svg+xml", 10, 10,
            new VectorMetrics(1, 4, new VectorBounds(0, 0, 10, 10, 10, 10)), DateTimeOffset.UtcNow));

        var simplificationService = new FakeSimplificationService();
        simplificationService.AddSimplification(new SimplificationVersion(
            ProjectId, ImageId, 3, SimplificationId, VectorId,
            new SimplificationParameters(0.004, "medium"),
            SimplificationStorageKey, "image/svg+xml", 10, 10,
            new SimplificationMetrics(
                new VectorMetrics(1, 12, new VectorBounds(0, 0, 10, 10, 10, 10)),
                new VectorMetrics(1, 4, new VectorBounds(0, 0, 10, 10, 10, 10)),
                66.7),
            DateTimeOffset.UtcNow));

        var dimensionService = new FakeDimensionService();
        dimensionService.AddDimension(new DimensionVersion(
            ProjectId, ImageId, 1, DimensionId, VectorId, DimensionSourceKind.Vector,
            new DimensionParameters(100, 100, true),
            DimensionStorageKey, "image/svg+xml", 10, 10, DateTimeOffset.UtcNow));

        var projectRegistry = new FakeProjectRegistry();
        if (fileName is not null)
        {
            projectRegistry.Save(new ProjectRecord(
                ProjectId, ImageId, fileName, "image/png", 1024, 10, 10, "uploaded", "project/image/original.png", null, DateTimeOffset.UtcNow));
        }

        var service = new ExportService(
            vectorizationService, simplificationService, dimensionService, projectRegistry,
            NullLogger<ExportService>.Instance);

        return (service, vectorizationService, simplificationService, dimensionService, projectRegistry);
    }

    [Fact]
    public void Resolve_WhenSourceIdIsEmpty_ReturnsValidationFailed()
    {
        var (service, _, _, _, _) = CreateService();

        var result = service.Resolve(ProjectId, ImageId, "vector", Guid.Empty);

        var failed = Assert.IsType<ExportResult.ValidationFailed>(result);
        Assert.Equal("invalid_parameters", failed.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("mask")]
    public void Resolve_WhenSourceKindIsUnknown_ReturnsValidationFailed(string? sourceKind)
    {
        var (service, _, _, _, _) = CreateService();

        var result = service.Resolve(ProjectId, ImageId, sourceKind, VectorId);

        var failed = Assert.IsType<ExportResult.ValidationFailed>(result);
        Assert.Equal("invalid_parameters", failed.Code);
    }

    [Theory]
    [InlineData("VECTOR")]
    [InlineData("Vector")]
    [InlineData(" vector ")]
    public void Resolve_SourceKindParsingIsCaseInsensitiveAndTrimmed(string sourceKind)
    {
        var (service, _, _, _, _) = CreateService();

        var result = service.Resolve(ProjectId, ImageId, sourceKind, VectorId);

        Assert.IsType<ExportResult.Ready>(result);
    }

    [Fact]
    public void Resolve_WhenSourceKindIsVectorAndItExists_ResolvesTheVectorStorageKey()
    {
        var (service, _, _, _, _) = CreateService();

        var result = service.Resolve(ProjectId, ImageId, "vector", VectorId);

        var ready = Assert.IsType<ExportResult.Ready>(result);
        Assert.Equal(VectorStorageKey, ready.SvgStorageKey);
        Assert.Equal("image/svg+xml", ready.ContentType);
        Assert.Equal(ExportSourceKind.Vector, ready.SourceKind);
        Assert.Equal(VectorId, ready.SourceId);
        Assert.Equal(2, ready.Version);
    }

    [Fact]
    public void Resolve_WhenSourceKindIsSimplificationAndItExists_ResolvesTheSimplificationStorageKey()
    {
        var (service, _, _, _, _) = CreateService();

        var result = service.Resolve(ProjectId, ImageId, "simplification", SimplificationId);

        var ready = Assert.IsType<ExportResult.Ready>(result);
        Assert.Equal(SimplificationStorageKey, ready.SvgStorageKey);
        Assert.Equal(ExportSourceKind.Simplification, ready.SourceKind);
        Assert.Equal(3, ready.Version);
    }

    [Fact]
    public void Resolve_WhenSourceKindIsDimensionAndItExists_ResolvesTheDimensionStorageKey()
    {
        var (service, _, _, _, _) = CreateService();

        var result = service.Resolve(ProjectId, ImageId, "dimension", DimensionId);

        var ready = Assert.IsType<ExportResult.Ready>(result);
        Assert.Equal(DimensionStorageKey, ready.SvgStorageKey);
        Assert.Equal(ExportSourceKind.Dimension, ready.SourceKind);
        Assert.Equal(1, ready.Version);
    }

    [Fact]
    public void Resolve_WhenSourceVectorDoesNotExist_ReturnsNotFound()
    {
        var (service, _, _, _, _) = CreateService();

        var result = service.Resolve(ProjectId, ImageId, "vector", Guid.NewGuid());

        Assert.IsType<ExportResult.NotFound>(result);
    }

    [Fact]
    public void Resolve_WhenSourceSimplificationDoesNotExist_ReturnsNotFound()
    {
        var (service, _, _, _, _) = CreateService();

        var result = service.Resolve(ProjectId, ImageId, "simplification", Guid.NewGuid());

        Assert.IsType<ExportResult.NotFound>(result);
    }

    [Fact]
    public void Resolve_WhenSourceDimensionDoesNotExist_ReturnsNotFound()
    {
        var (service, _, _, _, _) = CreateService();

        var result = service.Resolve(ProjectId, ImageId, "dimension", Guid.NewGuid());

        Assert.IsType<ExportResult.NotFound>(result);
    }

    [Fact]
    public void Resolve_DerivesTheFileNameFromTheOriginalProjectFileNameAndTheSourceKindAndVersion()
    {
        var (service, _, _, _, _) = CreateService(fileName: "diseño final.png");

        var result = service.Resolve(ProjectId, ImageId, "vector", VectorId);

        var ready = Assert.IsType<ExportResult.Ready>(result);
        Assert.Equal("diseño final-vector-v2.svg", ready.FileName);
    }

    [Fact]
    public void Resolve_WhenTheProjectRecordIsMissing_StillReturnsReadyWithAFallbackFileName()
    {
        var (service, _, _, _, _) = CreateService(fileName: null);

        var result = service.Resolve(ProjectId, ImageId, "vector", VectorId);

        var ready = Assert.IsType<ExportResult.Ready>(result);
        Assert.Equal($"{Vectify.Api.Export.ExportFileNameSanitizer.FallbackBaseName}-vector-v2.svg", ready.FileName);
    }

    [Fact]
    public void Resolve_CalledTwiceWithTheSameArguments_IsDeterministic()
    {
        var (service, _, _, _, _) = CreateService();

        var first = Assert.IsType<ExportResult.Ready>(service.Resolve(ProjectId, ImageId, "vector", VectorId));
        var second = Assert.IsType<ExportResult.Ready>(service.Resolve(ProjectId, ImageId, "vector", VectorId));

        Assert.Equal(first.SvgStorageKey, second.SvgStorageKey);
        Assert.Equal(first.FileName, second.FileName);
        Assert.Equal(first.Version, second.Version);
    }

    [Fact]
    public void Resolve_NeverGeneratesOrAppliesAnything()
    {
        // GenerateVectorAsync/PreviewAsync/ApplyAsync/ApplyAsync lanzan
        // NotSupportedException en los fakes -- si ExportService los llegara a
        // invocar, este test fallaría con esa excepción en vez de pasar.
        var (service, _, _, _, _) = CreateService();

        var result = service.Resolve(ProjectId, ImageId, "dimension", DimensionId);

        Assert.IsType<ExportResult.Ready>(result);
    }
}
