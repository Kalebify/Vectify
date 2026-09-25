using Vectify.Api.Contracts;
using Vectify.Api.Dimensioning;

namespace Vectify.Api.Tests.Export;

/// <summary>
/// IDimensionService en memoria para tests unitarios de ExportService:
/// permite registrar de antemano la/las DimensionVersion "ya aplicadas" que
/// FindDimension debe devolver. ApplyAsync no se usa desde ExportService
/// (solo FindDimension), así que lanza si se llega a invocar.
/// </summary>
internal sealed class FakeDimensionService : IDimensionService
{
    private readonly Dictionary<(Guid ProjectId, Guid ImageId, Guid DimensionId), DimensionVersion> _dimensions = new();

    public void AddDimension(DimensionVersion record) =>
        _dimensions[(record.ProjectId, record.ImageId, record.DimensionId)] = record;

    public Task<DimensionResult> ApplyAsync(
        Guid projectId, Guid imageId, DimensionRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException("ExportService no debería llamar a ApplyAsync.");

    public DimensionVersion? FindDimension(Guid projectId, Guid imageId, Guid dimensionId) =>
        _dimensions.GetValueOrDefault((projectId, imageId, dimensionId));
}
