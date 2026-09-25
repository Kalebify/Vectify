using Microsoft.Extensions.Logging.Abstractions;
using Vectify.Api.Checking;
using Vectify.Api.Clients;
using Vectify.Api.Contracts;
using Vectify.Api.Options;
using Vectify.Api.Simplification;
using Vectify.Api.Tests.Projects;
using Vectify.Api.Vectorization;

namespace Vectify.Api.Tests.Checking;

/// <summary>
/// Pruebas unitarias de CheckService: orquesta localizar el SVG de origen
/// (VectorVersion o SimplificationVersion, según sourceKind) + validación +
/// llamada a Python, SIN caché/lock/persistencia (a diferencia de
/// SimplificationService/VectorizationService) -- ver spec.md M1-S08,
/// Definition of Done: "sin modificar el SVG".
/// </summary>
public sealed class CheckServiceTests
{
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ImageId = Guid.NewGuid();
    private static readonly Guid SourceVectorId = Guid.NewGuid();
    private static readonly Guid SourceSimplificationId = Guid.NewGuid();
    private const string VectorStorageKey = "project/image/vectors/source.svg";
    private const string SimplificationStorageKey = "project/image/simplifications/source.svg";

    private static (
        CheckService Service,
        FakeFileStorage Storage,
        FakePythonCheckClient PythonClient) CreateService(
        bool withExistingVector = true, bool withExistingSimplification = true)
    {
        var vectorizationService = new FakeVectorizationService();
        if (withExistingVector)
        {
            vectorizationService.AddVector(new VectorVersion(
                ProjectId, ImageId, 1, SourceVectorId, Guid.NewGuid(), new VectorParameters(),
                VectorStorageKey, "image/svg+xml", 10, 10,
                new VectorMetrics(1, 4, new VectorBounds(0, 0, 10, 10, 10, 10)), DateTimeOffset.UtcNow));
        }

        var simplificationService = new FakeSimplificationService();
        if (withExistingSimplification)
        {
            simplificationService.AddSimplification(new SimplificationVersion(
                ProjectId, ImageId, 1, SourceSimplificationId, SourceVectorId,
                new SimplificationParameters(0.004, "medium"),
                SimplificationStorageKey, "image/svg+xml", 10, 10,
                new SimplificationMetrics(
                    new VectorMetrics(1, 12, new VectorBounds(0, 0, 10, 10, 10, 10)),
                    new VectorMetrics(1, 4, new VectorBounds(0, 0, 10, 10, 10, 10)),
                    66.7),
                DateTimeOffset.UtcNow));
        }

        var storage = new FakeFileStorage();
        storage.Saved[VectorStorageKey] = System.Text.Encoding.UTF8.GetBytes(
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\"><path d=\"M0,0 L10,0 L10,10 L0,10\"/></svg>");
        storage.Saved[SimplificationStorageKey] = System.Text.Encoding.UTF8.GetBytes(
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\"><path d=\"M0,0 L10,0 L10,10 L0,10 Z\"/></svg>");

        var validator = new CheckParameterValidator(Microsoft.Extensions.Options.Options.Create(new CheckOptions()));
        var pythonClient = new FakePythonCheckClient();

        var service = new CheckService(
            vectorizationService, simplificationService, validator, pythonClient, storage,
            NullLogger<CheckService>.Instance);

        return (service, storage, pythonClient);
    }

    private static CheckRequest VectorRequest(Guid? sourceId = null) =>
        new("vector", sourceId ?? SourceVectorId, null, null);

    private static CheckRequest SimplificationRequest(Guid? sourceId = null) =>
        new("simplification", sourceId ?? SourceSimplificationId, null, null);

    [Fact]
    public async Task AnalyzeAsync_WhenParametersAreInvalid_ReturnsValidationFailedWithoutCallingPython()
    {
        var (service, _, pythonClient) = CreateService();

        var result = await service.AnalyzeAsync(
            ProjectId, ImageId, new CheckRequest("unknown-kind", SourceVectorId, null, null), CancellationToken.None);

        var failed = Assert.IsType<CheckResult.ValidationFailed>(result);
        Assert.Equal("invalid_parameters", failed.Code);
        Assert.Equal(0, pythonClient.CallCount);
    }

    [Fact]
    public async Task AnalyzeAsync_WhenSourceVectorDoesNotExist_ReturnsNotFound()
    {
        var (service, _, _) = CreateService(withExistingVector: false);

        var result = await service.AnalyzeAsync(ProjectId, ImageId, VectorRequest(), CancellationToken.None);

        Assert.IsType<CheckResult.NotFound>(result);
    }

    [Fact]
    public async Task AnalyzeAsync_WhenSourceSimplificationDoesNotExist_ReturnsNotFound()
    {
        var (service, _, _) = CreateService(withExistingSimplification: false);

        var result = await service.AnalyzeAsync(ProjectId, ImageId, SimplificationRequest(), CancellationToken.None);

        Assert.IsType<CheckResult.NotFound>(result);
    }

    [Fact]
    public async Task AnalyzeAsync_WhenSourceKindIsVector_ResolvesTheVectorStorageKey()
    {
        var (service, _, pythonClient) = CreateService();

        var result = await service.AnalyzeAsync(ProjectId, ImageId, VectorRequest(), CancellationToken.None);

        var ready = Assert.IsType<CheckResult.Ready>(result);
        Assert.Equal(CheckSourceKind.Vector, ready.SourceKind);
        Assert.Equal(SourceVectorId, ready.SourceId);
        Assert.Equal(1, pythonClient.CallCount);
    }

    [Fact]
    public async Task AnalyzeAsync_WhenSourceKindIsSimplification_ResolvesTheSimplificationStorageKey()
    {
        var (service, _, pythonClient) = CreateService();

        var result = await service.AnalyzeAsync(ProjectId, ImageId, SimplificationRequest(), CancellationToken.None);

        var ready = Assert.IsType<CheckResult.Ready>(result);
        Assert.Equal(CheckSourceKind.Simplification, ready.SourceKind);
        Assert.Equal(SourceSimplificationId, ready.SourceId);
        Assert.Equal(1, pythonClient.CallCount);
    }

    [Fact]
    public async Task AnalyzeAsync_NeverPersistsAnythingToStorage()
    {
        var (service, storage, _) = CreateService();
        var savedCountBefore = storage.Saved.Count;

        await service.AnalyzeAsync(ProjectId, ImageId, VectorRequest(), CancellationToken.None);

        Assert.Equal(savedCountBefore, storage.Saved.Count);
    }

    [Fact]
    public async Task AnalyzeAsync_WhenCalledTwice_CallsPythonTwiceAndNeverCaches()
    {
        var (service, _, pythonClient) = CreateService();
        var request = VectorRequest();

        await service.AnalyzeAsync(ProjectId, ImageId, request, CancellationToken.None);
        await service.AnalyzeAsync(ProjectId, ImageId, request, CancellationToken.None);

        // Sin caché: este análisis nunca persiste nada, así que no hay nada
        // que reutilizar entre llamadas -- ver spec.md, Definition of Done.
        Assert.Equal(2, pythonClient.CallCount);
    }

    [Fact]
    public async Task AnalyzeAsync_ReturnsTheIssuesReportedByPython()
    {
        var (service, _, _) = CreateService();

        var result = await service.AnalyzeAsync(ProjectId, ImageId, VectorRequest(), CancellationToken.None);

        var ready = Assert.IsType<CheckResult.Ready>(result);
        var issue = Assert.Single(ready.Issues);
        Assert.IsType<CheckIssue.OpenPath>(issue);
    }

    [Fact]
    public async Task AnalyzeAsync_WhenPythonReportsInvalidInputSvg_ReturnsUpstreamErrorWithExpectedCode()
    {
        var (service, _, pythonClient) = CreateService();
        pythonClient.Respond = () => new PythonCheckResult(
            PythonCheckState.InvalidInputSvg, null, null, "SVG de entrada corrupto");

        var result = await service.AnalyzeAsync(ProjectId, ImageId, VectorRequest(), CancellationToken.None);

        var error = Assert.IsType<CheckResult.UpstreamError>(result);
        Assert.Equal("invalid_input_svg", error.Code);
    }

    [Fact]
    public async Task AnalyzeAsync_WhenPythonTimesOut_ReturnsUpstreamErrorWithTimeoutCode()
    {
        var (service, _, pythonClient) = CreateService();
        pythonClient.Respond = () => new PythonCheckResult(
            PythonCheckState.Timeout, null, null, "tardó demasiado");

        var result = await service.AnalyzeAsync(ProjectId, ImageId, VectorRequest(), CancellationToken.None);

        var error = Assert.IsType<CheckResult.UpstreamError>(result);
        Assert.Equal("timeout", error.Code);
    }

    [Fact]
    public async Task AnalyzeAsync_WhenPythonReportsTooManySubpaths_ReturnsUpstreamErrorWithExpectedCode()
    {
        var (service, _, pythonClient) = CreateService();
        pythonClient.Respond = () => new PythonCheckResult(
            PythonCheckState.TooManySubpaths, null, null, "demasiados subpaths");

        var result = await service.AnalyzeAsync(ProjectId, ImageId, VectorRequest(), CancellationToken.None);

        var error = Assert.IsType<CheckResult.UpstreamError>(result);
        Assert.Equal("too_many_subpaths", error.Code);
    }
}
